using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.ConfigurationItems;
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

        // The management server reports OAuth bearer-token standalone SDK sessions (e.g. integrations
        // that log in via AddServerOAuth with a basic user) under this placeholder identity - it
        // cannot map the token back to the basic user name. We relabel it so the row reads as an
        // intentional integration rather than an "unknown" user.
        private const string UnknownOAuthIdentity = "Unknown OAuth user";
        private const string OAuthIntegrationLabel = "Integration / SDK client";

        private static readonly Regex IdentityRegex =
            new Regex(@"^\s*(?<name>.*?)\s*(\((?<ip>[^)]*)\))?\s*$", RegexOptions.Compiled);

        private readonly object _lock = new object();

        // ObjectId -> camera name, for all enabled cameras (the denominator).
        private Dictionary<Guid, string> _enabledCameras = new Dictionary<Guid, string>();
        // ObjectId -> the recording server that owns the camera (its FQID.ServerId.Uri),
        // captured during the same tree walk and used to query stream statistics.
        private Dictionary<Guid, Uri> _cameraRecorderUri = new Dictionary<Guid, Uri>();
        // Recording-server base Uri -> display name, enumerated authoritatively from the Management
        // Server configuration. Drives the storage panel so every recording server shows up - even
        // ones whose cameras this login can't see (the device tree only surfaced one recorder).
        private Dictionary<Uri, string> _recorders = new Dictionary<Uri, string>();
        // ObjectId -> device-tree folder path (e.g. "video-hq-rec1 / Building A"), captured by walking
        // the Management Client system hierarchy. Optional enrichment for the "Folder" grouping.
        private Dictionary<Guid, string> _cameraFolder = new Dictionary<Guid, string>();
        // ObjectId -> last reported state string, merged from ProvideCurrentState responses.
        private readonly Dictionary<Guid, string> _deviceStates = new Dictionary<Guid, string>();
        private List<UserRow> _users = new List<UserRow>();
        // Roles + their assigned members (enumerated from config), and the match-keys of the users
        // currently connected (from WhoAreOnline). Used by the "Folder & Role" view item to show, per
        // role, how many assigned users are logged in.
        private List<RoleInfo> _roles = new List<RoleInfo>();
        private HashSet<string> _connectedUserKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>One role and the identity match-keys of each assigned user (built once from config).</summary>
        private sealed class RoleInfo
        {
            public string Name;
            public readonly List<HashSet<string>> Members = new List<HashSet<string>>();
            public int Total => Members.Count;
        }

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
            LoadRoles();

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
                var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                        // Remember the connected identity (pre-relabel) for role membership matching.
                        connected.UnionWith(IdentityKeys(user));

                        // Relabel the server's "Unknown OAuth user" placeholder for standalone OAuth
                        // sessions (see the constant above) to a friendlier integration label.
                        if (serverType.Equals("Standalone", StringComparison.OrdinalIgnoreCase) &&
                            user.Equals(UnknownOAuthIdentity, StringComparison.OrdinalIgnoreCase))
                            user = OAuthIntegrationLabel;

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

                lock (_lock) { _users = users; _connectedUserKeys = connected; }
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
            if (!_closing) { LoadEnabledCameras(); LoadRoles(); }
            RaiseStatusChanged();
            return null;
        }

        // ── Snapshot building ─────────────────────────────────────────────────

        private void LoadEnabledCameras()
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                var map = new Dictionary<Guid, string>();
                var recMap = new Dictionary<Guid, Uri>();
                var recorders = new Dictionary<Uri, string>();

                // Authoritative path: enumerate every recording server (and its cameras) from the
                // Management Server configuration. This is independent of the device-tree scoping
                // that was only surfacing one recorder. Fall back to the device-tree walk only if
                // the config API yields nothing (older sites / restricted config access).
                bool viaConfig = LoadFromConfig(map, recMap, recorders);
                if (!viaConfig || map.Count == 0)
                    LoadFromDeviceTree(map, recMap);

                // Best-effort: capture each camera's device-tree folder path for the "Folder" grouping.
                // Independent of the camera source above; cameras the walk misses just have no path.
                var folders = new Dictionary<Guid, string>();
                try { BuildFolderMap(folders); }
                catch (Exception ex) { Log.Error("Folder map build failed", ex); }

                lock (_lock)
                {
                    _enabledCameras = map; _cameraRecorderUri = recMap;
                    _recorders = recorders; _cameraFolder = folders;
                }
                sw.Stop();
                Log.Info($"Loaded {map.Count} enabled camera(s) across {recorders.Count} recorder(s) " +
                         $"(source: {(viaConfig ? "config" : "device tree")}); {folders.Count} folder path(s) " +
                         $"in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Log.Error("LoadEnabledCameras failed", ex);
            }
        }

        /// <summary>
        /// Enumerates recording servers and their enabled cameras from the Management Server config
        /// (the same API RemoteManager / PKI / CertWatchdog use). Populates the camera-name map, the
        /// per-camera recorder Uri, and the recorder list (base Uri -> display name). Returns false
        /// (logged) if the config API is unavailable, so the caller can fall back to the device tree.
        /// </summary>
        private bool LoadFromConfig(Dictionary<Guid, string> map, Dictionary<Guid, Uri> recMap,
                                    Dictionary<Uri, string> recorders)
        {
            try
            {
                var management = new ManagementServer(EnvironmentManager.Instance.MasterSite);
                int rsCount = 0;
                foreach (var rs in management.RecordingServerFolder.RecordingServers)
                {
                    if (rs == null || !rs.Enabled) continue;
                    var baseUri = RecorderBaseUri(rs);
                    if (baseUri == null) { Log.Info($"Recording server '{rs.Name}' has no usable web Uri - skipped"); continue; }
                    rsCount++;
                    if (!recorders.ContainsKey(baseUri))
                        recorders[baseUri] = string.IsNullOrEmpty(rs.Name) ? baseUri.Host : rs.Name;

                    int cams = 0;
                    foreach (var hw in rs.HardwareFolder.Hardwares)
                    {
                        if (hw == null || !hw.Enabled) continue;
                        foreach (var cam in hw.CameraFolder.Cameras)
                        {
                            if (cam == null || !cam.Enabled) continue;
                            if (!Guid.TryParse(cam.Id, out var id)) continue;
                            map[id] = cam.Name;
                            recMap[id] = baseUri;
                            cams++;
                        }
                    }
                    Log.Info($"Recording server '{rs.Name}' @ {baseUri} : {cams} enabled camera(s)");
                }
                return rsCount > 0;
            }
            catch (Exception ex)
            {
                Log.Error("Config API recorder enumeration failed - falling back to device-tree walk", ex);
                return false;
            }
        }

        // The recorder's web base Uri (host:7563), from the published WebServerUri/ActiveWebServerUri,
        // falling back to HostName. This is the valid RecorderStatusService2 base - unlike the
        // Kind.Server item Uri, which 404s on the status path.
        private static Uri RecorderBaseUri(RecordingServer rs)
        {
            string raw = null;
            try { raw = string.IsNullOrEmpty(rs.ActiveWebServerUri) ? rs.WebServerUri : rs.ActiveWebServerUri; }
            catch { }
            if (!string.IsNullOrEmpty(raw) && Uri.TryCreate(raw, UriKind.Absolute, out var u)) return u;
            try { var h = rs.HostName; if (!string.IsNullOrEmpty(h)) return new Uri($"http://{h}:7563/"); }
            catch { }
            return null;
        }

        /// <summary>
        /// Fallback enumeration: walk the system device tree from the Kind.Camera roots and collect
        /// enabled camera leaves, mapping each to its owning recorder via FQID.ServerId.Uri. Does not
        /// populate the recorder list (the storage panel is then camera-derived, as before).
        /// </summary>
        private void LoadFromDeviceTree(Dictionary<Guid, string> map, Dictionary<Guid, Uri> recMap)
        {
            var roots = Configuration.Instance.GetItemsByKind(Kind.Camera, ItemHierarchy.SystemDefined)
                        ?? new List<Item>();
            var visited = new HashSet<Guid>();
            var stack = new Stack<KeyValuePair<Item, int>>();
            foreach (var r in roots) stack.Push(new KeyValuePair<Item, int>(r, 0));

            while (stack.Count > 0)
            {
                var pair = stack.Pop();
                var it = pair.Key;
                var depth = pair.Value;
                if (it?.FQID == null || depth > 16) continue;
                if (!visited.Add(it.FQID.ObjectId)) continue;

                if (it.FQID.Kind == Kind.Camera && it.FQID.FolderType == FolderType.No)
                {
                    if (it.Enabled)
                    {
                        map[it.FQID.ObjectId] = it.Name;
                        var recUri = it.FQID.ServerId?.Uri;
                        if (recUri != null) recMap[it.FQID.ObjectId] = recUri;
                    }
                    continue;
                }

                List<Item> kids = null;
                try { kids = it.GetChildren(); } catch { }
                if (kids != null)
                    foreach (var k in kids) stack.Push(new KeyValuePair<Item, int>(k, depth + 1));
            }
        }

        // Fixed wrapper nodes in the camera tree ("All Cameras" flatly holds every camera; "Camera
        // Groups" wraps the device groups). They carry no grouping meaning, so they are dropped from
        // the folder path - leaving "<server> / <group> / <subgroup>".
        private static readonly HashSet<string> FolderWrapperNodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All Cameras", "Camera Groups" };

        // Bucket for cameras that belong to no device group.
        private const string NoFolderLabel = "(no folder)";

        /// <summary>
        /// Records each enabled camera's folder path from the device-group tree - the camera "folders"
        /// laid out in Management Client (Devices, Cameras). MIP exposes that tree as the
        /// UserDefined hierarchy (the SystemDefined one is a flat "All Cameras" list, which is why
        /// grouping on it produced a single group). Path = the chain of group names down to the camera,
        /// e.g. "Building A / Floor 2". The "All Cameras" catch-all is dropped from the path; a camera
        /// that lives only there (in no real group) is left without a folder. Cameras that belong to
        /// several groups take the first one walked.
        /// </summary>
        private void BuildFolderMap(Dictionary<Guid, string> folders)
        {
            var roots = Configuration.Instance.GetItemsByKind(Kind.Camera, ItemHierarchy.UserDefined)
                        ?? new List<Item>();

            var visited = new HashSet<Guid>();
            var stack = new Stack<KeyValuePair<Item, string>>();
            foreach (var r in roots) stack.Push(new KeyValuePair<Item, string>(r, null));

            while (stack.Count > 0)
            {
                var pair = stack.Pop();
                var it = pair.Key;
                var path = pair.Value;
                if (it?.FQID == null) continue;
                if (!visited.Add(it.FQID.ObjectId)) continue;

                if (it.FQID.Kind == Kind.Camera && it.FQID.FolderType == FolderType.No)
                {
                    if (it.Enabled && !string.IsNullOrEmpty(path)) folders[it.FQID.ObjectId] = path;
                    continue;
                }

                if (path != null && path.Length > 400) continue;   // guard against pathological depth
                var name = string.IsNullOrWhiteSpace(it.Name) ? "?" : it.Name.Trim();
                // Pass the path straight through the fixed wrapper nodes so only real groups show.
                var childPath = FolderWrapperNodes.Contains(name)
                    ? path
                    : (string.IsNullOrEmpty(path) ? name : path + " / " + name);

                List<Item> kids = null;
                try { kids = it.GetChildren(); } catch { }
                if (kids != null)
                    foreach (var k in kids) stack.Push(new KeyValuePair<Item, string>(k, childPath));
            }
        }

        // ── Roles (assigned users vs currently logged in) ─────────────────────
        /// <summary>
        /// Enumerates all roles and their assigned users from the Management Server config (RoleFolder),
        /// caching each member's identity match-keys for the "Folder &amp; Role" view item. Heavy config
        /// call; runs on Init and on configuration changes, off the UI thread. Includes empty roles.
        /// </summary>
        private void LoadRoles()
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var site = EnvironmentManager.Instance.MasterSite;
                var ms = new ManagementServer(site);
                var serverId = site.ServerId;
                var list = new List<RoleInfo>();

                foreach (var role in ms.RoleFolder.Roles)
                {
                    if (role == null) continue;
                    var info = new RoleInfo { Name = string.IsNullOrWhiteSpace(role.DisplayName) ? role.Name : role.DisplayName };
                    try
                    {
                        var full = new Role(serverId, role.Path);
                        foreach (var u in full.UserFolder.Users)
                        {
                            if (u == null) continue;
                            var keys = IdentityKeys(u.Name);
                            keys.UnionWith(IdentityKeys(u.DisplayName));
                            if (keys.Count > 0) info.Members.Add(keys);
                        }
                    }
                    catch (Exception ex) { Log.Error($"Role members failed for '{info.Name}'", ex); }
                    list.Add(info);
                }

                lock (_lock) { _roles = list; }
                sw.Stop();
                Log.Info($"Loaded {list.Count} role(s), {list.Sum(r => r.Total)} member assignment(s) in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                Log.Error("LoadRoles failed", ex);
            }
        }

        // Normalized identity match-keys: the raw string plus the bare account (after "DOMAIN\" or
        // before "@"), so a connected "DOMAIN\jdoe" / "jdoe@dom" / "jdoe" all match a role member.
        private static HashSet<string> IdentityKeys(string identity)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(identity)) return set;
            var s = identity.Trim();
            set.Add(s);
            int bs = s.LastIndexOf('\\');
            if (bs >= 0 && bs < s.Length - 1) set.Add(s.Substring(bs + 1));
            int at = s.IndexOf('@');
            if (at > 0) set.Add(s.Substring(0, at));
            return set;
        }

        // ── View-item data (folder camera status + role user status) ──────────
        /// <summary>
        /// Per camera-folder online/total device counts (for the "Folder &amp; Role" view item). When
        /// <paramref name="includeServerPrefix"/> is false the leading "server / " segment is dropped,
        /// so same-named groups across recorders merge into one row.
        /// </summary>
        public IReadOnlyList<FolderStatusRow> GetFolderCameraStatus(bool includeServerPrefix)
        {
            var snap = CurrentSnapshot;
            var folderMap = SnapshotFolderMap();
            var online = new Dictionary<string, int>();
            var total = new Dictionary<string, int>();
            foreach (var c in snap.Cameras)
            {
                folderMap.TryGetValue(c.Id, out var f);
                var key = FolderLabel(f, includeServerPrefix);
                total[key] = total.TryGetValue(key, out var t) ? t + 1 : 1;
                if (c.Online) online[key] = online.TryGetValue(key, out var o) ? o + 1 : 1;
            }
            return total.Keys
                .OrderBy(k => k == NoFolderLabel)   // real folders first, "(no folder)" last
                .ThenBy(k => k, StringComparer.CurrentCultureIgnoreCase)
                .Select(k => new FolderStatusRow
                {
                    Folder = k,
                    Online = online.TryGetValue(k, out var o) ? o : 0,
                    Total = total[k]
                })
                .ToList();
        }

        // The folder grouping key/label: the full device-group path, or - when the server prefix is
        // off - the path with its leading "server" segment removed (cameras directly under the server
        // with no real group then fall under "(no folder)").
        private static string FolderLabel(string fullPath, bool includeServerPrefix)
        {
            if (string.IsNullOrEmpty(fullPath)) return NoFolderLabel;
            if (includeServerPrefix) return fullPath;
            int i = fullPath.IndexOf(" / ", StringComparison.Ordinal);
            return i >= 0 ? fullPath.Substring(i + 3) : NoFolderLabel;
        }

        /// <summary>Per role, how many assigned users are currently logged in (for the view item).</summary>
        public IReadOnlyList<RoleStatusRow> GetRoleUserStatus()
        {
            List<RoleInfo> roles; HashSet<string> connected;
            lock (_lock) { roles = _roles; connected = _connectedUserKeys; }
            return roles
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new RoleStatusRow
                {
                    Role = r.Name,
                    Total = r.Total,
                    LoggedIn = r.Members.Count(m => m.Overlaps(connected))
                })
                .ToList();
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
