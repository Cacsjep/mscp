using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using BarcodeReader.Messaging;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;

namespace BarcodeReader.Background
{
    public class BarcodeReaderBackgroundPlugin : BackgroundPlugin
    {
        private static readonly PluginLog _log = new PluginLog("BarcodeReader");
        private readonly SystemLog _sysLog = new SystemLog(_log);
        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);

        private object _configMessageObj;
        private readonly ConcurrentDictionary<Guid, HelperProcess> _helpers = new ConcurrentDictionary<Guid, HelperProcess>();
        private Timer _monitorTimer;
        private volatile bool _closing;
        private string _helperExePath;
        private string _serverUri;
        private string _milestoneDir;
        private string _lastConfigSnapshot;

        // QR Code library cache: exact payload text -> list of matching QR item FQIDs.
        // Rebuilt on config-change alongside helper reload. Multiple FQIDs are allowed in
        // the list for defensive reasons only  the admin UI forbids duplicate payloads at
        // save time, so the list is normally size 1, but a crash-mid-save can't corrupt us.
        private readonly ConcurrentDictionary<string, List<FQID>> _payloadToQRItems =
            new ConcurrentDictionary<string, List<FQID>>(StringComparer.Ordinal);

        public override Guid Id => BarcodeReaderDefinition.BackgroundPluginId;
        public override string Name => "Barcode Reader Background";
        public override List<EnvironmentType> TargetEnvironments => new List<EnvironmentType> { EnvironmentType.Service };

        public override void Init()
        {
            _log.Info("Background plugin initializing");

            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _helperExePath = Path.Combine(pluginDir, "BarcodeReaderHelper.exe");

            if (!File.Exists(_helperExePath))
            {
                _log.Error($"Helper exe not found: {_helperExePath}");
                return;
            }
            _log.Info($"Helper exe: {_helperExePath}");

            _milestoneDir = Path.GetDirectoryName(typeof(EnvironmentManager).Assembly.Location);
            _log.Info($"Milestone dir: {_milestoneDir}");

            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                _serverUri = $"{serverId.ServerScheme}://{serverId.ServerHostname}:{serverId.Serverport}";
                _log.Info($"Management server URI: {_serverUri}");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to determine management server URI: {ex.Message}", ex);
                return;
            }

            // Subscribe to ConfigurationChangedIndication for BOTH our item kinds: channel
            // edits affect helpers, QR Code edits affect the payload cache. A single filter
            // can only carry one RelatedKind, so we register a single generic subscription
            // and rely on the snapshot compare in OnConfigurationChanged to cheap-skip when
            // nothing we care about changed.
            _configMessageObj = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdFilter(MessageId.Server.ConfigurationChangedIndication));

            _sysLog.Register();

            _cmh.Start();
            _cmh.Register(OnStatusRequest, new CommunicationIdFilter(BarcodeMessageIds.StatusRequest));

            LoadQRCodeCache();
            LoadAndStartChannels();

            _monitorTimer = new Timer(MonitorHelpers, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        private void LoadQRCodeCache()
        {
            try
            {
                var qrItems = Configuration.Instance.GetItemConfigurations(
                    BarcodeReaderDefinition.PluginId, null, BarcodeReaderDefinition.QRCodeKindId);

                var fresh = new Dictionary<string, List<FQID>>(StringComparer.Ordinal);
                foreach (var item in qrItems)
                {
                    if (!item.Properties.ContainsKey(QRCodeConfig_KeyPayload)) continue;
                    var payload = item.Properties[QRCodeConfig_KeyPayload];
                    if (string.IsNullOrEmpty(payload)) continue;

                    if (!fresh.TryGetValue(payload, out var list))
                    {
                        list = new List<FQID>();
                        fresh[payload] = list;
                    }
                    list.Add(item.FQID);
                }

                _payloadToQRItems.Clear();
                foreach (var kv in fresh) _payloadToQRItems[kv.Key] = kv.Value;

                _log.Info($"QR Code cache loaded: {_payloadToQRItems.Count} payload(s)");
            }
            catch (Exception ex)
            {
                _log.Error("Failed to load QR Code cache", ex);
            }
        }

        // String constant mirror of QRCodeConfig.KeyPayload. Duplicating here instead of
        // referencing the Admin type avoids pulling the admin-only namespace into the
        // Event Server's loaded type graph.
        private const string QRCodeConfig_KeyPayload = "Payload";

        public override void Close()
        {
            _log.Info("Background plugin closing, stopping all helpers");
            _closing = true;

            _monitorTimer?.Dispose();
            _monitorTimer = null;

            if (_configMessageObj != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_configMessageObj);
                _configMessageObj = null;
            }

            _cmh.Close();
            StopAllHelpers();
        }

        private void LoadAndStartChannels()
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    BarcodeReaderDefinition.PluginId, null, BarcodeReaderDefinition.PluginKindId);

                foreach (var item in items)
                {
                    var cfg = ChannelConfig.FromItem(item);
                    if (!cfg.Enabled) continue;
                    if (cfg.CameraId == Guid.Empty) continue;

                    LaunchHelper(cfg);
                }

                _lastConfigSnapshot = GetConfigSnapshot();
                _log.Info($"Loaded channels. Active helpers: {_helpers.Count}");
            }
            catch (Exception ex)
            {
                _log.Error($"Error loading config: {ex.Message}", ex);
            }
        }

        private void LaunchHelper(ChannelConfig cfg)
        {
            if (_helpers.TryRemove(cfg.ItemId, out var existing))
                KillHelper(existing);

            try
            {
                _log.Info($"Launching helper: {cfg.Name} camera={cfg.CameraName}");

                // Arg order must match BarcodeReaderHelper Program.cs
                var args = string.Join(" ", new[]
                {
                    $"\"{_serverUri}\"",
                    $"\"{cfg.CameraId}\"",
                    $"\"{cfg.ItemId}\"",
                    $"\"{cfg.Formats}\"",
                    cfg.TryHarder    ? "1" : "0",
                    cfg.AutoRotate   ? "1" : "0",
                    cfg.TryInverted  ? "1" : "0",
                    cfg.TargetFps.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    cfg.DownscaleWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    cfg.DebounceMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    cfg.CreateBookmarks ? "1" : "0",
                    $"\"{(cfg.Name ?? "").Replace("\"", "'")}\"",
                    $"\"{_milestoneDir}\""
                });

                var psi = new ProcessStartInfo
                {
                    FileName = _helperExePath,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    _log.Error($"Failed to start helper process for {cfg.Name}");
                    return;
                }

                var helper = new HelperProcess
                {
                    Process = process,
                    Config = cfg,
                    RestartCount = 0
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    OnHelperLine(helper, e.Data);
                };
                process.BeginErrorReadLine();

                _helpers[cfg.ItemId] = helper;
                _sysLog.ChannelStarted(cfg.Name, cfg.CameraName);
                _log.Info($"Helper launched: PID={process.Id}, channel={cfg.Name}");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to launch helper for {cfg.Name}: {ex.Message}", ex);
            }
        }

        private void OnHelperLine(HelperProcess helper, string line)
        {
            if (line.StartsWith("STATS "))
            {
                ParseStats(helper, line);
                TransmitStatusUpdate(helper.Config.ItemId);
                return;
            }

            if (line.StartsWith("STATUS "))
            {
                var newStatus = line.Substring(7);
                var prev = helper.LastStatus ?? "";
                helper.LastStatus = newStatus;

                if (newStatus.StartsWith("Error") && !prev.StartsWith("Error"))
                    _sysLog.ChannelError(helper.Config.Name, newStatus);
                else if (newStatus == "Stopped" && prev != "Stopped")
                    _sysLog.ChannelStopped(helper.Config.Name);

                TransmitStatusUpdate(helper.Config.ItemId, force: true);
                return;
            }

            if (line.StartsWith("DETECT "))
            {
                // DETECT <unixMs> <format> <base64Text>
                var parts = line.Split(new[] { ' ' }, 4);
                if (parts.Length == 4)
                {
                    string text;
                    try { text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[3])); }
                    catch { text = "<invalid-base64>"; }

                    var logLine = $"DETECT [{helper.Config.Name}] {parts[2]}: {text}";
                    _log.Info(logLine);
                    helper.AddLogLine($"{DateTime.Now:HH:mm:ss.fff} INFO  {parts[2]}: {text}");
                    TransmitStatusUpdate(helper.Config.ItemId);

                    FireDetectionEvents(helper, parts[2], text);
                }
                return;
            }

            if (IsNoiseLine(line)) return;

            helper.AddLogLine(line);
            _log.Info($"[Helper:{helper.Config.Name}] {line}");
            TransmitStatusUpdate(helper.Config.ItemId);
        }

        private void FireDetectionEvents(HelperProcess helper, string format, string text)
        {
            // Always: "Barcode Detected" scoped to the scanning channel.
            var channelFqid = new FQID
            {
                ServerId = EnvironmentManager.Instance.MasterSite.ServerId,
                ObjectId = helper.Config.ItemId,
                Kind = BarcodeReaderDefinition.PluginKindId,
                FolderType = FolderType.No,
            };
            SendAnalyticsEvent(
                eventType: "BarcodeDetected",
                registeredMessage: "Barcode Detected",
                sourceName: helper.Config.Name,
                sourceFqid: channelFqid,
                customTag: text);

            // If the decoded text exactly matches a stored QR item's payload, also fire
            // "QR Code Matched" scoped to that QR item. Admin UI rejects duplicate
            // payloads so this is typically a single match; the loop tolerates leftovers
            // from a legacy config that predates the uniqueness check.
            if (_payloadToQRItems.TryGetValue(text, out var matches) && matches != null)
            {
                foreach (var qrFqid in matches)
                {
                    SendAnalyticsEvent(
                        eventType: "QRCodeMatched",
                        registeredMessage: "QR Code Matched",
                        sourceName: ResolveName(qrFqid),
                        sourceFqid: qrFqid,
                        customTag: text);
                }
            }
        }

        private string ResolveName(FQID fqid)
        {
            try
            {
                var item = Configuration.Instance.GetItemConfiguration(
                    BarcodeReaderDefinition.PluginId, fqid.Kind, fqid.ObjectId);
                return item?.Name ?? "";
            }
            catch { return ""; }
        }

        private void SendAnalyticsEvent(string eventType, string registeredMessage,
            string sourceName, FQID sourceFqid, string customTag)
        {
            try
            {
                var header = new EventHeader
                {
                    ID = Guid.NewGuid(),
                    Class = "Operational",
                    Type = eventType,
                    Timestamp = DateTime.Now,
                    Name = sourceName,
                    // Message MUST equal the registered EventType.Message verbatim so the
                    // Rules engine can match this analytics event to the event definition.
                    Message = registeredMessage,
                    CustomTag = customTag ?? "",
                    Priority = 3,
                    Source = new EventSource { Name = sourceName, FQID = sourceFqid }
                };
                var ev = new AnalyticsEvent { EventHeader = header };
                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.Server.NewEventCommand)
                    {
                        Data = ev,
                        RelatedFQID = sourceFqid
                    });
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to send {eventType} event: {ex.Message}");
            }
        }

        // Drop log lines that carry no actionable information. Specifically the Milestone
        // LiveStatusEventArgs default ToString() just prints the type name  older helper
        // builds emitted those in a tight loop and spammed both the Event Server log and
        // the admin UI's helper-log grid. Keep this defensive filter in case similar junk
        // ever reappears.
        private static bool IsNoiseLine(string line)
        {
            return line.IndexOf("LiveStatus: VideoOS.Platform", StringComparison.Ordinal) >= 0;
        }

        private void StopAllHelpers()
        {
            foreach (var kvp in _helpers) KillHelper(kvp.Value);
            _helpers.Clear();
        }

        private void KillHelper(HelperProcess helper)
        {
            try
            {
                if (helper.Process != null && !helper.Process.HasExited)
                {
                    _log.Info($"Killing helper PID={helper.Process.Id} ({helper.Config.Name})");
                    helper.Process.Kill();
                    helper.Process.WaitForExit(3000);
                }
            }
            catch (Exception ex) { _log.Error($"Error killing helper: {ex.Message}"); }
            finally { try { helper.Process?.Dispose(); } catch { } }
        }

        private void MonitorHelpers(object state)
        {
            if (_closing) return;

            foreach (var kvp in _helpers)
            {
                var helper = kvp.Value;
                try
                {
                    if (helper.Process == null || helper.Process.HasExited)
                    {
                        var exitCode = helper.Process?.ExitCode ?? -1;
                        var restartCount = helper.RestartCount + 1;
                        var reason = MapExitCode(exitCode);
                        _log.Error($"Helper died (exit={exitCode} {reason}): {helper.Config.Name}, restart #{restartCount}");

                        // Surface the last few stderr lines so MIPLog explains *what* the
                        // helper was doing right before it went away. Without this you only
                        // see "exit=255" in MIPLog and the cause is invisible. We also dump
                        // the on-disk helper log path so support can grab the full trail.
                        var tail = helper.GetLogSnapshot();
                        if (tail.Count > 0)
                        {
                            _log.Error($"Helper last {tail.Count} stderr line(s) for {helper.Config.Name}:");
                            foreach (var ln in tail) _log.Error($"  | {ln}");
                        }
                        var helperLogPath = TryGetHelperLogPath(helper.Config.ItemId);
                        if (!string.IsNullOrEmpty(helperLogPath))
                            _log.Error($"Helper on-disk log: {helperLogPath}");

                        _sysLog.HelperCrashed(helper.Config.Name, restartCount);

                        // Remove and dispose BEFORE LaunchHelper runs. Otherwise LaunchHelper's
                        // own TryRemove+KillHelper path would hit a disposed Process and log
                        // "No process is associated with this object."
                        _helpers.TryRemove(helper.Config.ItemId, out _);
                        try { helper.Process?.Dispose(); } catch { }
                        helper.Process = null;

                        LaunchHelper(helper.Config);

                        if (_helpers.TryGetValue(helper.Config.ItemId, out var newHelper))
                            newHelper.RestartCount = restartCount;
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Monitor error for {helper.Config.Name}: {ex.Message}");
                }
            }
        }

        private string GetConfigSnapshot()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var items = Configuration.Instance.GetItemConfigurations(
                    BarcodeReaderDefinition.PluginId, null, BarcodeReaderDefinition.PluginKindId);

                foreach (var item in items)
                {
                    sb.Append(item.FQID.ObjectId);
                    foreach (var key in ChannelConfig.PropertyKeys)
                    {
                        if (item.Properties.ContainsKey(key)) sb.Append(item.Properties[key]);
                    }
                    sb.Append("|");
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        private object OnConfigurationChanged(Message message, FQID dest, FQID sender)
        {
            var currentConfig = GetConfigSnapshot();
            if (currentConfig == _lastConfigSnapshot) return null;

            _log.Info("Channel configuration changed, reloading helpers");
            _lastConfigSnapshot = currentConfig;

            try
            {
                LoadQRCodeCache();
                StopAllHelpers();
                LoadAndStartChannels();
            }
            catch (Exception ex) { _log.Error($"Error reloading config: {ex.Message}", ex); }

            return null;
        }

        private ChannelStatusUpdate BuildStatusUpdate(Guid itemId)
        {
            if (!_helpers.TryGetValue(itemId, out var helper))
            {
                return new ChannelStatusUpdate
                {
                    ItemId = itemId,
                    Status = "Stopped",
                    RecentLogLines = new List<string>(),
                    Timestamp = DateTime.UtcNow
                };
            }

            return new ChannelStatusUpdate
            {
                ItemId = itemId,
                Status = helper.LastStatus ?? "Starting",
                Frames = helper.Frames,
                Decoded = helper.Decoded,
                Failed = helper.Failed,
                CameraFps = helper.CameraFps,
                DecodeFps = helper.DecodeFps,
                InfMsAvg = helper.InfMsAvg,
                InfMsP95 = helper.InfMsP95,
                MaxFps = helper.MaxFps,
                RestartCount = helper.RestartCount,
                RecentLogLines = helper.GetLogSnapshot(),
                Timestamp = DateTime.UtcNow
            };
        }

        private void TransmitStatusUpdate(Guid itemId, bool force = false)
        {
            if (_cmh.MessageCommunication == null) return;

            if (!force && _helpers.TryGetValue(itemId, out var h))
            {
                var now = DateTime.UtcNow;
                if ((now - h.LastTransmitTime).TotalMilliseconds < 500) return;
                h.LastTransmitTime = now;
            }

            try
            {
                var update = BuildStatusUpdate(itemId);
                _cmh.TransmitMessage(new Message(BarcodeMessageIds.StatusUpdate, update));
            }
            catch (Exception ex) { _log.Error($"Failed to transmit status update: {ex.Message}"); }
        }

        private object OnStatusRequest(Message message, FQID dest, FQID source)
        {
            var request = message.Data as ChannelStatusRequest;
            if (request == null) return null;

            try
            {
                var update = BuildStatusUpdate(request.ItemId);
                _cmh.TransmitMessage(new Message(BarcodeMessageIds.StatusResponse, update));
            }
            catch (Exception ex) { _log.Error($"Failed to send status response: {ex.Message}"); }
            return null;
        }

        // Mirror of the exit-code constants in BarcodeReaderHelper.Program. Keep in sync.
        // Values outside this range are produced by the OS terminating the process from
        // outside our managed try/catch  almost always a native delay-load failure
        // (SEH 0xc06d007e, surfaces as exit code 255) or a stack/AV. When you see
        // "NativeCrash" here, follow up with Process Monitor to find the missing DLL.
        private static string MapExitCode(int code)
        {
            switch (code)
            {
                case 0:  return "Stopped";
                case 1:  return "BadArgs";
                case 2:  return "InvalidCameraId";
                case 3:  return "CameraNotFound";
                case 4:  return "ManagedException";
                case 5:  return "EnvironmentMissing";
                case 6:  return "NativeDepsMissing";
                case -1: return "NoExitCode";
                case 255: return "NativeCrash";
                default: return $"Unknown({code})";
            }
        }

        private static string TryGetHelperLogPath(Guid itemId)
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Milestone", "BarcodeReader", $"helper-{itemId}.log");
            }
            catch { return null; }
        }

        private static void ParseStats(HelperProcess helper, string line)
        {
            foreach (var part in line.Split(' '))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                var inv = System.Globalization.CultureInfo.InvariantCulture;

                switch (kv[0])
                {
                    case "frames":      long.TryParse(kv[1], out helper.Frames); break;
                    case "decoded":     long.TryParse(kv[1], out helper.Decoded); break;
                    case "failed":      long.TryParse(kv[1], out helper.Failed); break;
                    case "cam_fps":     double.TryParse(kv[1], System.Globalization.NumberStyles.Float, inv, out helper.CameraFps); break;
                    case "decode_fps":  double.TryParse(kv[1], System.Globalization.NumberStyles.Float, inv, out helper.DecodeFps); break;
                    case "inf_ms_avg":  double.TryParse(kv[1], System.Globalization.NumberStyles.Float, inv, out helper.InfMsAvg); break;
                    case "inf_ms_p95":  double.TryParse(kv[1], System.Globalization.NumberStyles.Float, inv, out helper.InfMsP95); break;
                    case "max_fps":     double.TryParse(kv[1], System.Globalization.NumberStyles.Float, inv, out helper.MaxFps); break;
                }
            }
        }

        private class HelperProcess
        {
            public Process Process;
            public ChannelConfig Config;
            public int RestartCount;
            public volatile string LastStatus;

            public long Frames, Decoded, Failed;
            public double CameraFps, DecodeFps, InfMsAvg, InfMsP95, MaxFps;
            public DateTime LastTransmitTime;

            private const int MaxLogLines = 40;
            private readonly object _logLock = new object();
            private readonly Queue<string> _logLines = new Queue<string>();

            public void AddLogLine(string line)
            {
                lock (_logLock)
                {
                    if (_logLines.Count >= MaxLogLines) _logLines.Dequeue();
                    _logLines.Enqueue(line);
                }
            }

            public List<string> GetLogSnapshot()
            {
                lock (_logLock) { return new List<string>(_logLines); }
            }
        }
    }
}
