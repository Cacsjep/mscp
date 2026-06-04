using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Messaging;

namespace SystemStatus.Background
{
    /// <summary>
    /// Session-lived owner of all system-status data. It holds one MessageCommunication channel
    /// to the Event Server and uses it for two things:
    ///   - WhoAreOnline      -> connected users
    ///   - ProvideCurrentState -> per-camera "Responding" state
    /// The toolbar button and the flyout are thin consumers that subscribe to <see cref="StatusChanged"/>
    /// and read <see cref="CurrentSnapshot"/>; they never query anything themselves.
    /// </summary>
    public partial class SystemStatusBackgroundPlugin : BackgroundPlugin
    {
        internal static SystemStatusBackgroundPlugin Instance { get; private set; }

        private static readonly PluginLog Log = new PluginLog("SystemStatus - SC BG");

        // Event Server device states reported via ProvideCurrentState (confirmed in VideoOS.Platform.SDK.dll).
        private const string StateResponding = "Responding";

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

        // TEST: inject fake cameras to preview the list with many entries (online state arbitrary).
        // Set to false to disable.
        private static readonly bool InjectFakeCameras = false;
        private static readonly List<CameraRow> FakeCameras = BuildFakeCameras();
        private static List<CameraRow> BuildFakeCameras()
        {
            var list = new List<CameraRow>();
            for (int i = 1; i <= 300; i++)
                list.Add(new CameraRow
                {
                    Id = Guid.NewGuid(),
                    Name = $"Fake Camera {i:000} - AXIS P3265-V Dome Camera (10.0.0.{i}) - Channel 1",
                    Online = i % 4 != 0
                });
            return list;
        }

        // Endpoint ServerType values we never show as connected users.
        private static readonly HashSet<string> HiddenServerTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Service" };

        private static readonly Regex IdentityRegex =
            new Regex(@"^\s*(?<name>.*?)\s*(\((?<ip>[^)]*)\))?\s*$", RegexOptions.Compiled);

        private readonly object _lock = new object();

        // ObjectId -> camera name, for all enabled cameras (the denominator).
        private Dictionary<Guid, string> _enabledCameras = new Dictionary<Guid, string>();
        // ObjectId -> the recording server that owns the camera (its FQID.ServerId.Uri),
        // captured during the same tree walk and used to query stream statistics.
        private Dictionary<Guid, Uri> _cameraRecorderUri = new Dictionary<Guid, Uri>();
        // ObjectId -> last reported state string, merged from ProvideCurrentState responses.
        private readonly Dictionary<Guid, string> _deviceStates = new Dictionary<Guid, string>();
        private List<UserRow> _users = new List<UserRow>();

        private ServerId _serverId;
        private object _whoFilter;
        private object _stateFilter;
        private object _endPointChangedFilter;
        private object _configReceiver;
        private Timer _timer;
        private volatile bool _closing;

        internal StatusSnapshot CurrentSnapshot { get; private set; } = StatusSnapshot.Empty;

        /// <summary>Raised whenever the snapshot changes. Handlers must marshal to the UI thread.</summary>
        internal event EventHandler<StatusChangedEventArgs> StatusChanged;

        public override Guid Id => SystemStatusDefinition.BackgroundPluginId;
        public override string Name => "System Status Smart Client";
        public override List<EnvironmentType> TargetEnvironments { get; } =
            new List<EnvironmentType> { EnvironmentType.SmartClient };

        public override void Init()
        {
            Instance = this;
            Log.Info("Init - log: %ProgramData%\\Milestone\\XProtect Smart Client\\MIPLog.txt");

            LoadEnabledCameras();

            // Reload the enabled-camera set when the configuration changes.
            _configReceiver = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdFilter(MessageId.Server.ConfigurationChangedIndication));

            try
            {
                _serverId = EnvironmentManager.Instance.MasterSite?.ServerId;
                if (_serverId == null)
                {
                    Log.Error("MasterSite not available - cannot start message communication");
                }
                else
                {
                    MessageCommunicationManager.Start(_serverId);
                    var mc = MessageCommunicationManager.Get(_serverId);

                    _whoFilter = mc.RegisterCommunicationFilter(
                        OnWhoAreOnlineResponse,
                        new CommunicationIdFilter(MessageCommunication.WhoAreOnlineResponse));
                    _stateFilter = mc.RegisterCommunicationFilter(
                        OnProvideCurrentStateResponse,
                        new CommunicationIdFilter(MessageCommunication.ProvideCurrentStateResponse));
                    // The set of connected endpoints changing is a good trigger to re-ask who is online.
                    _endPointChangedFilter = mc.RegisterCommunicationFilter(
                        OnEndPointTableChanged,
                        new CommunicationIdFilter(MessageCommunication.EndPointTableChangedIndication));
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to start message communication", ex);
            }

            // Periodic refresh + an immediate first poll (timer fires immediately, then every interval).
            _timer = new Timer(_ => SafeRefresh(), null, TimeSpan.FromSeconds(1), RefreshInterval);

            RaiseStatusChanged(); // publish the initial (cameras known, nothing online yet) snapshot
            Log.Info($"Initialized - {_enabledCameras.Count} enabled camera(s), refresh every {RefreshInterval.TotalSeconds:0}s");
        }

        public override void Close()
        {
            _closing = true;
            try { _timer?.Dispose(); } catch { }
            _timer = null;

            if (_configReceiver != null)
            {
                try { EnvironmentManager.Instance.UnRegisterReceiver(_configReceiver); } catch { }
                _configReceiver = null;
            }

            if (_serverId != null)
            {
                try
                {
                    var mc = MessageCommunicationManager.Get(_serverId);
                    if (_whoFilter != null) mc.UnRegisterCommunicationFilter(_whoFilter);
                    if (_stateFilter != null) mc.UnRegisterCommunicationFilter(_stateFilter);
                    if (_endPointChangedFilter != null) mc.UnRegisterCommunicationFilter(_endPointChangedFilter);
                }
                catch { }
                try { MessageCommunicationManager.Stop(_serverId); } catch { }
            }
            _whoFilter = _stateFilter = _endPointChangedFilter = null;
            Log.Info("Closed");
        }

        // ── Requests ──────────────────────────────────────────────────────────

        private void SafeRefresh()
        {
            if (_closing) return;
            try
            {
                if (_serverId == null) return;
                var mc = MessageCommunicationManager.Get(_serverId);
                if (mc == null || !mc.IsConnected) return;

                // Both requests broadcast to the server (null destination/source), matching the
                // WhoAreOnline / StatusViewer sample pattern.
                mc.TransmitMessage(new Message(MessageCommunication.WhoAreOnlineRequest), null, null, null);
                mc.TransmitMessage(new Message(MessageCommunication.ProvideCurrentStateRequest), null, null, null);
            }
            catch (Exception ex)
            {
                Log.Error("Refresh request failed", ex);
            }
        }

        // ── Responses (run on MIP communication threads) ──────────────────────

        private object OnWhoAreOnlineResponse(Message message, FQID destination, FQID source)
        {
            try
            {
                var rawObjs = new List<object>();
                if (message.Data is System.Collections.IEnumerable seq)
                    foreach (var o in seq) { if (o != null) rawObjs.Add(o); }
                else if (message.Data != null)
                    rawObjs.Add(message.Data);

                // Group connected endpoints by user + client type. The client type lives on the
                // (runtime) ServerId.ServerType of each endpoint's FQID: SmartClient, Administration,
                // Standalone, Service, ManagementServer. Service endpoints are hidden. A single client
                // can register several endpoints, so we count them per (user, type).
                var groups = new Dictionary<string, UserGroup>(StringComparer.OrdinalIgnoreCase);
                foreach (var o in rawObjs)
                {
                    try
                    {
                        var ep = o as EndPointIdentityData;
                        if (ep == null) continue;

                        var serverType = TryGetString(ep.EndPointFQID?.ServerId, "ServerType") ?? "";
                        if (HiddenServerTypes.Contains(serverType)) continue;

                        var user = ParseUserName(ep.IdentityName);
                        if (string.IsNullOrWhiteSpace(user)) continue;

                        // Hide the "network service" standalone endpoint (system account, not a user).
                        if (serverType.Equals("Standalone", StringComparison.OrdinalIgnoreCase) &&
                            user.IndexOf("network service", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        var friendly = FriendlyClientType(serverType);
                        var key = user + "|" + friendly;
                        if (!groups.TryGetValue(key, out var g))
                            groups[key] = g = new UserGroup { User = user, Type = friendly };
                        g.Count++;
                    }
                    catch (Exception exItem)
                    {
                        Log.Error($"endpoint parse failed: {exItem.Message}");
                    }
                }

                var users = groups.Values
                    .OrderBy(g => g.User, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(g => g.Type, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => new UserRow
                    {
                        DisplayName = g.User,
                        Secondary = g.Count > 1 ? $"{g.Type} (x{g.Count})" : g.Type
                    })
                    .ToList();

                Log.Info($"WhoAreOnline: {rawObjs.Count} endpoint(s) -> {users.Count} user/client row(s)");

                lock (_lock) { _users = users; }
                RaiseStatusChanged();
            }
            catch (Exception ex)
            {
                Log.Error("OnWhoAreOnlineResponse failed", ex);
            }
            return null;
        }

        private object OnProvideCurrentStateResponse(Message message, FQID destination, FQID source)
        {
            try
            {
                var states = AsEnumerable(message.Data)?.OfType<ItemState>().ToList();
                if (states == null || states.Count == 0) return null;

                lock (_lock)
                {
                    foreach (var s in states)
                    {
                        if (s?.FQID == null) continue;
                        _deviceStates[s.FQID.ObjectId] = s.State;
                    }
                }
                RaiseStatusChanged();
            }
            catch (Exception ex)
            {
                Log.Error("OnProvideCurrentStateResponse failed", ex);
            }
            return null;
        }

        private object OnEndPointTableChanged(Message message, FQID destination, FQID source)
        {
            // Connected clients changed - refresh promptly rather than waiting for the timer.
            SafeRefresh();
            return null;
        }

        private object OnConfigurationChanged(Message message, FQID destination, FQID sender)
        {
            if (!_closing) LoadEnabledCameras();
            RaiseStatusChanged();
            return null;
        }

        // ── Snapshot building ─────────────────────────────────────────────────

        private void LoadEnabledCameras()
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // GetItemsByKind(Kind.Camera) returns only the device-tree roots (a container named
                // after the recording server, e.g. "acs") - not the cameras. Walk the tree from those
                // roots and collect the actual camera devices (Kind.Camera + FolderType.No). Iterative
                // (stack) rather than recursive so ~1000 cameras stays cheap and shallow on the stack.
                var roots = Configuration.Instance.GetItemsByKind(Kind.Camera, ItemHierarchy.SystemDefined)
                            ?? new List<Item>();

                var map = new Dictionary<Guid, string>();
                var recMap = new Dictionary<Guid, Uri>();
                var visited = new HashSet<Guid>();
                var stack = new Stack<KeyValuePair<Item, int>>();
                foreach (var r in roots) stack.Push(new KeyValuePair<Item, int>(r, 0));

                int scanned = 0, containers = 0;
                while (stack.Count > 0)
                {
                    var pair = stack.Pop();
                    var it = pair.Key;
                    var depth = pair.Value;
                    if (it?.FQID == null || depth > 16) continue;
                    if (!visited.Add(it.FQID.ObjectId)) continue;
                    scanned++;

                    if (it.FQID.Kind == Kind.Camera && it.FQID.FolderType == FolderType.No)
                    {
                        if (it.Enabled)
                        {
                            map[it.FQID.ObjectId] = it.Name; // leaf camera
                            // FQID.ServerId is the recording server that owns the camera; its Uri
                            // is the RecorderStatusService2 endpoint (the documented stats pattern).
                            var recUri = it.FQID.ServerId?.Uri;
                            if (recUri != null) recMap[it.FQID.ObjectId] = recUri;
                        }
                        continue;
                    }

                    containers++;
                    List<Item> kids = null;
                    try { kids = it.GetChildren(); } catch { }
                    if (kids != null)
                        foreach (var k in kids) stack.Push(new KeyValuePair<Item, int>(k, depth + 1));
                }

                lock (_lock) { _enabledCameras = map; _cameraRecorderUri = recMap; }
                sw.Stop();
                Log.Info($"Loaded {map.Count} enabled camera(s); scanned {scanned} node(s), " +
                         $"{containers} container(s), from {roots.Count} root(s) in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Log.Error("LoadEnabledCameras failed", ex);
            }
        }

        private void RaiseStatusChanged()
        {
            StatusSnapshot snap;
            lock (_lock)
            {
                var rows = _enabledCameras
                    .Select(kv =>
                    {
                        _deviceStates.TryGetValue(kv.Key, out var state);
                        return new CameraRow
                        {
                            Id = kv.Key,
                            Name = kv.Value,
                            Online = string.Equals(state, StateResponding, StringComparison.OrdinalIgnoreCase)
                        };
                    })
                    .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (InjectFakeCameras) rows.AddRange(FakeCameras);

                var users = _users
                    .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                snap = new StatusSnapshot(rows, users, rows.Count(r => r.Online), rows.Count);
            }

            CurrentSnapshot = snap;
            try { StatusChanged?.Invoke(this, new StatusChangedEventArgs(snap)); }
            catch (Exception ex) { Log.Error("StatusChanged handler threw", ex); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // ServerId.ServerType is exposed only on the runtime subtype, so read it reflectively.
        private static string TryGetString(object o, string member)
        {
            if (o == null) return null;
            try
            {
                var p = o.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return p.GetValue(o)?.ToString();
                var f = o.GetType().GetField(member, BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(o)?.ToString();
            }
            catch { }
            return null;
        }

        // "admin  (fe80::...)" -> "admin"
        private static string ParseUserName(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return string.Empty;
            var m = IdentityRegex.Match(identity);
            var name = m.Groups["name"].Value.Trim();
            return string.IsNullOrEmpty(name) ? identity.Trim() : name;
        }

        private static string FriendlyClientType(string serverType)
        {
            switch (serverType)
            {
                case "SmartClient": return "Smart Client";
                case "Administration": return "Management Client";
                case "ManagementServer": return "Management Server";
                case "Standalone": return "Standalone";
                case "Service": return "Service";
                default: return string.IsNullOrEmpty(serverType) ? "Unknown" : serverType;
            }
        }

        private sealed class UserGroup
        {
            public string User;
            public string Type;
            public int Count;
        }

        private static System.Collections.IEnumerable AsEnumerable(object data)
        {
            // Response Data may arrive as List<T> or T[]; both are IEnumerable.
            return data as System.Collections.IEnumerable;
        }
    }
}
