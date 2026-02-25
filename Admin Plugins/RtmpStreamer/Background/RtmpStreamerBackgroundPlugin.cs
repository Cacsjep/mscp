using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using RTMPStreamer.Messaging;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Messaging;

namespace RTMPStreamer.Background
{
    public class RTMPStreamerBackgroundPlugin : BackgroundPlugin
    {
        private object _configMessageObj;
        private readonly ConcurrentDictionary<Guid, HelperProcess> _helpers = new ConcurrentDictionary<Guid, HelperProcess>();
        private Timer _monitorTimer;
        private volatile bool _closing;
        private string _helperExePath;
        private string _serverUri;
        private string _milestoneDir;
        private string _lastConfigSnapshot;

        private MessageCommunication _mc;
        private object _statusRequestFilter;

        public override Guid Id => RTMPStreamerDefinition.BackgroundPluginId;
        public override string Name => "RTMP Streamer Background";

        public override System.Collections.Generic.List<EnvironmentType> TargetEnvironments
        {
            get { return new System.Collections.Generic.List<EnvironmentType> { EnvironmentType.Service }; }
        }

        public override void Init()
        {
            PluginLog.Info("Background plugin initializing");

            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _helperExePath = Path.Combine(pluginDir, "RTMPStreamerHelper.exe");

            if (!File.Exists(_helperExePath))
            {
                PluginLog.Error($"Helper exe not found: {_helperExePath}");
                return;
            }

            PluginLog.Info($"Helper exe: {_helperExePath}");

            _milestoneDir = Path.GetDirectoryName(typeof(EnvironmentManager).Assembly.Location);
            PluginLog.Info($"Milestone dir: {_milestoneDir}");

            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                _serverUri = $"{serverId.ServerScheme}://{serverId.ServerHostname}:{serverId.Serverport}";
                PluginLog.Info($"Management server URI: {_serverUri}");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to determine management server URI: {ex.Message}", ex);
                return;
            }

            _configMessageObj = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdAndRelatedKindFilter(
                    MessageId.Server.ConfigurationChangedIndication,
                    RTMPStreamerDefinition.PluginKindId));

            SystemLog.Register();

            try
            {
                var mcServerId = EnvironmentManager.Instance.MasterSite.ServerId;
                MessageCommunicationManager.Start(mcServerId);
                _mc = MessageCommunicationManager.Get(mcServerId);
                _statusRequestFilter = _mc.RegisterCommunicationFilter(
                    OnStatusRequest,
                    new CommunicationIdFilter(StreamMessageIds.StatusRequest));
                PluginLog.Info("MessageCommunication initialized for live status");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to init MessageCommunication: {ex.Message}", ex);
            }

            LoadAndStartStreams();

            SystemLog.PluginStarted(_helpers.Count);

            _monitorTimer = new Timer(MonitorHelpers, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        public override void Close()
        {
            PluginLog.Info("Background plugin closing, stopping all helpers");
            _closing = true;

            _monitorTimer?.Dispose();
            _monitorTimer = null;

            if (_configMessageObj != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_configMessageObj);
                _configMessageObj = null;
            }

            if (_mc != null && _statusRequestFilter != null)
            {
                _mc.UnRegisterCommunicationFilter(_statusRequestFilter);
                _statusRequestFilter = null;
            }
            _mc = null;

            SystemLog.PluginStopped();

            StopAllHelpers();
        }

        private void LoadAndStartStreams()
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    RTMPStreamerDefinition.PluginId, null, RTMPStreamerDefinition.PluginKindId);

                foreach (var item in items)
                {
                    // Each item is one stream with its own properties
                    var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
                    if (!enabled) continue;

                    if (!item.Properties.ContainsKey("CameraId") || !item.Properties.ContainsKey("RtmpUrl"))
                        continue;

                    if (!Guid.TryParse(item.Properties["CameraId"], out var cameraId) || cameraId == Guid.Empty)
                        continue;

                    var rtmpUrl = item.Properties["RtmpUrl"];
                    if (string.IsNullOrEmpty(rtmpUrl)) continue;

                    var cameraName = item.Properties.ContainsKey("CameraName") ? item.Properties["CameraName"] : "";
                    var allowUntrustedCerts = item.Properties.ContainsKey("AllowUntrustedCerts")
                        && item.Properties["AllowUntrustedCerts"] == "Yes";

                    LaunchHelper(item.FQID.ObjectId, cameraId, cameraName, rtmpUrl, allowUntrustedCerts);
                }

                _lastConfigSnapshot = GetConfigSnapshot();
                PluginLog.Info($"Loaded streams. Active helpers: {_helpers.Count}");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error loading config: {ex.Message}", ex);
            }
        }

        private void LaunchHelper(Guid itemId, Guid cameraId, string cameraName, string rtmpUrl, bool allowUntrustedCerts = false)
        {
            if (_helpers.TryRemove(itemId, out var existing))
                KillHelper(existing);

            try
            {
                PluginLog.Info($"Launching helper: {cameraName} ({cameraId}) -> {rtmpUrl}");

                var psi = new ProcessStartInfo
                {
                    FileName = _helperExePath,
                    Arguments = $"\"{_serverUri}\" \"{cameraId}\" \"{rtmpUrl}\" \"{_milestoneDir}\" \"{(allowUntrustedCerts ? "true" : "false")}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    PluginLog.Error($"Failed to start helper process for {cameraName}");
                    return;
                }

                var helper = new HelperProcess
                {
                    Process = process,
                    ItemId = itemId,
                    CameraId = cameraId,
                    CameraName = cameraName,
                    RtmpUrl = rtmpUrl,
                    AllowUntrustedCerts = allowUntrustedCerts,
                    RestartCount = 0
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    if (e.Data.StartsWith("STATS "))
                    {
                        ParseStats(helper, e.Data);
                        TransmitStatusUpdate(helper.ItemId);
                        return;
                    }

                    if (e.Data.StartsWith("STATUS "))
                    {
                        var newStatus = e.Data.Substring(7);
                        var prev = helper.LastStatus ?? "";
                        helper.LastStatus = newStatus;

                        // Only persist significant state changes to avoid constant config saves.
                        // Transient states (Connecting, Reconnecting, Initializing, Waiting)
                        // are shown live via MessageCommunication but don't update item properties.
                        if (newStatus.StartsWith("Streaming") ||
                            newStatus.StartsWith("Error") ||
                            newStatus.StartsWith("Codec") ||
                            newStatus == "Stopped")
                        {
                            // Write to Milestone System Log on significant transitions
                            if (newStatus.StartsWith("Streaming") && !prev.StartsWith("Streaming"))
                                SystemLog.StreamConnected(cameraName, rtmpUrl);
                            else if ((newStatus.StartsWith("Error") || newStatus.StartsWith("Codec")) &&
                                     !prev.StartsWith("Error") && !prev.StartsWith("Codec"))
                                SystemLog.StreamError(cameraName, newStatus);
                            else if (newStatus == "Stopped" && prev != "Stopped")
                                SystemLog.StreamStopped(cameraName);
                        }

                        TransmitStatusUpdate(helper.ItemId, force: true);
                        return;
                    }

                    helper.AddLogLine(e.Data);
                    PluginLog.Info($"[Helper:{cameraName}] {e.Data}");
                    TransmitStatusUpdate(helper.ItemId);
                };
                process.BeginErrorReadLine();

                _helpers[itemId] = helper;
                PluginLog.Info($"Helper launched: PID={process.Id}, camera={cameraName}");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to launch helper for {cameraName}: {ex.Message}", ex);
            }
        }

        private void StopAllHelpers()
        {
            foreach (var kvp in _helpers)
                KillHelper(kvp.Value);
            _helpers.Clear();
        }

        private void KillHelper(HelperProcess helper)
        {
            try
            {
                if (helper.Process != null && !helper.Process.HasExited)
                {
                    PluginLog.Info($"Killing helper PID={helper.Process.Id} ({helper.CameraName})");
                    helper.Process.Kill();
                    helper.Process.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error killing helper: {ex.Message}");
            }
            finally
            {
                try { helper.Process?.Dispose(); } catch { }
            }
        }

        private void MonitorHelpers(object state)
        {
            if (_closing) return;

            foreach (var kvp in _helpers)
            {
                var itemId = kvp.Key;
                var helper = kvp.Value;

                try
                {
                    if (helper.Process == null || helper.Process.HasExited)
                    {
                        var exitCode = helper.Process?.ExitCode ?? -1;
                        var restartCount = helper.RestartCount + 1;
                        PluginLog.Info($"Helper died (exit={exitCode}): {helper.CameraName}, restart #{restartCount}");
                        SystemLog.HelperCrashed(helper.CameraName, restartCount);

                        try { helper.Process?.Dispose(); } catch { }
                        LaunchHelper(itemId, helper.CameraId, helper.CameraName, helper.RtmpUrl, helper.AllowUntrustedCerts);

                        if (_helpers.TryGetValue(itemId, out var newHelper))
                            newHelper.RestartCount = restartCount;
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Monitor error for {helper.CameraName}: {ex.Message}");
                }
            }

        }

        private string GetConfigSnapshot()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var items = Configuration.Instance.GetItemConfigurations(
                    RTMPStreamerDefinition.PluginId, null, RTMPStreamerDefinition.PluginKindId);

                foreach (var item in items)
                {
                    sb.Append(item.FQID.ObjectId);
                    if (item.Properties.ContainsKey("CameraId")) sb.Append(item.Properties["CameraId"]);
                    if (item.Properties.ContainsKey("RtmpUrl")) sb.Append(item.Properties["RtmpUrl"]);
                    if (item.Properties.ContainsKey("Enabled")) sb.Append(item.Properties["Enabled"]);
                    if (item.Properties.ContainsKey("AllowUntrustedCerts")) sb.Append(item.Properties["AllowUntrustedCerts"]);
                    sb.Append("|");
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        private object OnConfigurationChanged(Message message, FQID dest, FQID sender)
        {
            var currentConfig = GetConfigSnapshot();
            if (currentConfig == _lastConfigSnapshot)
                return null;

            PluginLog.Info("Stream configuration changed, reloading helpers");
            _lastConfigSnapshot = currentConfig;

            try
            {
                StopAllHelpers();
                LoadAndStartStreams();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error reloading config: {ex.Message}", ex);
            }

            return null;
        }

        private StreamStatusUpdate BuildStatusUpdate(Guid itemId)
        {
            if (!_helpers.TryGetValue(itemId, out var helper))
            {
                return new StreamStatusUpdate
                {
                    ItemId = itemId,
                    Status = "Stopped",
                    RecentLogLines = new List<string>(),
                    Timestamp = DateTime.UtcNow
                };
            }

            return new StreamStatusUpdate
            {
                ItemId = itemId,
                Status = helper.LastStatus ?? "Starting",
                Frames = helper.Frames,
                Fps = helper.Fps,
                Bytes = helper.Bytes,
                KeyFrames = helper.KeyFrames,
                RestartCount = helper.RestartCount,
                RecentLogLines = helper.GetLogSnapshot(),
                Timestamp = DateTime.UtcNow
            };
        }

        private void TransmitStatusUpdate(Guid itemId, bool force = false)
        {
            if (_mc == null) return;

            if (!force && _helpers.TryGetValue(itemId, out var h))
            {
                var now = DateTime.UtcNow;
                if ((now - h.LastTransmitTime).TotalMilliseconds < 500)
                    return;
                h.LastTransmitTime = now;
            }

            try
            {
                var update = BuildStatusUpdate(itemId);
                _mc.TransmitMessage(
                    new Message(StreamMessageIds.StatusUpdate, update), null, null, null);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to transmit status update: {ex.Message}");
            }
        }

        private object OnStatusRequest(Message message, FQID dest, FQID source)
        {
            var request = message.Data as StreamStatusRequest;
            if (request == null) return null;

            var update = BuildStatusUpdate(request.ItemId);
            if (_mc != null)
            {
                try
                {
                    _mc.TransmitMessage(
                        new Message(StreamMessageIds.StatusResponse, update), null, null, null);
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Failed to send status response: {ex.Message}");
                }
            }
            return null;
        }

        private static void ParseStats(HelperProcess helper, string line)
        {
            foreach (var part in line.Split(' '))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;

                switch (kv[0])
                {
                    case "frames":
                        long.TryParse(kv[1], out helper.Frames);
                        break;
                    case "fps":
                        double.TryParse(kv[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out helper.Fps);
                        break;
                    case "bytes":
                        long.TryParse(kv[1], out helper.Bytes);
                        break;
                    case "keyframes":
                        long.TryParse(kv[1], out helper.KeyFrames);
                        break;
                }
            }
        }

        private class HelperProcess
        {
            public Process Process;
            public Guid ItemId;
            public Guid CameraId;
            public string CameraName;
            public string RtmpUrl;
            public bool AllowUntrustedCerts;
            public int RestartCount;
            public volatile string LastStatus;
            public long Frames;
            public double Fps;
            public long Bytes;
            public long KeyFrames;
            public DateTime LastTransmitTime;

            private const int MaxLogLines = 40;
            private readonly object _logLock = new object();
            private readonly Queue<string> _logLines = new Queue<string>();

            public void AddLogLine(string line)
            {
                lock (_logLock)
                {
                    if (_logLines.Count >= MaxLogLines)
                        _logLines.Dequeue();
                    _logLines.Enqueue(line);
                }
            }

            public List<string> GetLogSnapshot()
            {
                lock (_logLock)
                {
                    return new List<string>(_logLines);
                }
            }
        }
    }
}
