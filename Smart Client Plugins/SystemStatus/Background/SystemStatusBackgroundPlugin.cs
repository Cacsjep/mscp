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
        // Recording-server base Uri -> the federated site that owns it. Used to pick the per-site
        // session token for that recorder's status service and to label rows by site.
        private Dictionary<Uri, SiteRef> _recorderSite = new Dictionary<Uri, SiteRef>();
        // ObjectId -> the federated site that owns the camera (display label / grouping).
        private Dictionary<Guid, string> _cameraSiteName = new Dictionary<Guid, string>();
        // ObjectId -> device-tree folder path (e.g. "video-hq-rec1 / Building A"), captured by walking
        // the Management Client system hierarchy. Optional enrichment for the "Folder" grouping.
        private Dictionary<Guid, string> _cameraFolder = new Dictionary<Guid, string>();
        // ObjectId -> last reported state string, merged from ProvideCurrentState responses (all sites).
        private readonly Dictionary<Guid, string> _deviceStates = new Dictionary<Guid, string>();
        // The master site plus every federated child site, walked at Init. One entry on a standalone
        // system; the recorder/camera/user/role enumeration and message channels run once per entry.
        private List<SiteRef> _sites = new List<SiteRef>();
        // Connected users and their match-keys bucketed by the site (ServerId.Id) that reported them,
        // so each site's WhoAreOnline response replaces only its own bucket. Combined into _users /
        // _connectedUserKeys whenever a bucket changes.
        private readonly Dictionary<Guid, List<UserRow>> _usersBySite = new Dictionary<Guid, List<UserRow>>();
        private readonly Dictionary<Guid, HashSet<string>> _connectedKeysBySite = new Dictionary<Guid, HashSet<string>>();
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

        /// <summary>One site in the (possibly federated) hierarchy: the master or a child site.</summary>
        private sealed class SiteRef
        {
            public string Name;
            public FQID Fqid;         // the site FQID - used for ManagementServer + LoginSettingsCache
            public ServerId ServerId; // the site's server - used for the message channel
        }

        /// <summary>One per-site Event Server message channel and its registered filter handles.</summary>
        private sealed class ChannelReg
        {
            public ServerId ServerId;
            public object Who, State, EndPoint;
        }

        // One message channel per site (master + federated children). Built at Init, torn down at Close.
        private readonly List<ChannelReg> _channels = new List<ChannelReg>();
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

            // Walk the (possibly federated) site hierarchy once; everything below runs per site.
            _sites = EnumerateSites();
            Log.Info($"Sites: {_sites.Count} ({string.Join(", ", _sites.Select(s => s.Name))})");

            LoadEnabledCameras();
            LoadRoles();

            // Reload the enabled-camera set when the configuration changes.
            _configReceiver = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdFilter(MessageId.Server.ConfigurationChangedIndication));

            // One Event Server message channel per site (WhoAreOnline + ProvideCurrentState). Child
            // sites are already authenticated by the Smart Client, so no explicit AddServer/Login here.
            foreach (var site in _sites) StartChannel(site);

            // Pre-warm every camera's recording range in the background, gently, starting now - so the
            // health window shows ranges already loaded (or "pending") instead of stampeding the
            // recorders with one query per camera the moment the window opens.
            StartRangePrewarm();

            // Periodic refresh + an immediate first poll (timer fires immediately, then every interval).
            _timer = new Timer(_ => SafeRefresh(), null, TimeSpan.FromSeconds(1), RefreshInterval);

            RaiseStatusChanged(); // publish the initial (cameras known, nothing online yet) snapshot
            Log.Info($"Initialized - {_enabledCameras.Count} enabled camera(s) across {_sites.Count} site(s), refresh every {RefreshInterval.TotalSeconds:0}s");
        }

        public override void Close()
        {
            _closing = true;
            StopRangePrewarm();
            try { _timer?.Dispose(); } catch { }
            _timer = null;

            if (_configReceiver != null)
            {
                try { EnvironmentManager.Instance.UnRegisterReceiver(_configReceiver); } catch { }
                _configReceiver = null;
            }

            foreach (var ch in _channels)
            {
                try
                {
                    var mc = MessageCommunicationManager.Get(ch.ServerId);
                    if (mc != null)
                    {
                        if (ch.Who != null) mc.UnRegisterCommunicationFilter(ch.Who);
                        if (ch.State != null) mc.UnRegisterCommunicationFilter(ch.State);
                        if (ch.EndPoint != null) mc.UnRegisterCommunicationFilter(ch.EndPoint);
                    }
                }
                catch { }
                try { MessageCommunicationManager.Stop(ch.ServerId); } catch { }
            }
            _channels.Clear();
            Log.Info("Closed");
        }

        // ── Requests ──────────────────────────────────────────────────────────

        private void SafeRefresh()
        {
            if (_closing) return;
            // Ask every site's Event Server who is online and for the current device states. A site
            // whose channel is down is skipped silently and retried on the next tick.
            foreach (var ch in _channels)
            {
                try
                {
                    var mc = MessageCommunicationManager.Get(ch.ServerId);
                    if (mc == null || !mc.IsConnected) continue;

                    // Both requests broadcast to the server (null destination/source), matching the
                    // WhoAreOnline / StatusViewer sample pattern.
                    mc.TransmitMessage(new Message(MessageCommunication.WhoAreOnlineRequest), null, null, null);
                    mc.TransmitMessage(new Message(MessageCommunication.ProvideCurrentStateRequest), null, null, null);
                }
                catch (Exception ex)
                {
                    Log.Error("Refresh request failed for a site channel", ex);
                }
            }
        }

        // Start one Event Server message channel for a site and register the three response filters.
        private void StartChannel(SiteRef site)
        {
            if (site?.ServerId == null) return;
            try
            {
                MessageCommunicationManager.Start(site.ServerId);
                var mc = MessageCommunicationManager.Get(site.ServerId);
                var reg = new ChannelReg { ServerId = site.ServerId };
                reg.Who = mc.RegisterCommunicationFilter(
                    OnWhoAreOnlineResponse,
                    new CommunicationIdFilter(MessageCommunication.WhoAreOnlineResponse));
                reg.State = mc.RegisterCommunicationFilter(
                    OnProvideCurrentStateResponse,
                    new CommunicationIdFilter(MessageCommunication.ProvideCurrentStateResponse));
                // The set of connected endpoints changing is a good trigger to re-ask who is online.
                reg.EndPoint = mc.RegisterCommunicationFilter(
                    OnEndPointTableChanged,
                    new CommunicationIdFilter(MessageCommunication.EndPointTableChangedIndication));
                _channels.Add(reg);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start message channel for site '{site.Name}'", ex);
            }
        }

        // Walk the master site and, in a federated hierarchy, its child sites (recursively). A site
        // Item's GetChildren() returns its federated child-site Items; the Smart Client has already
        // loaded and authenticated them, so no explicit login is needed here.
        private List<SiteRef> EnumerateSites()
        {
            var list = new List<SiteRef>();
            var masterFqid = EnvironmentManager.Instance.MasterSite;
            if (masterFqid == null)
            {
                Log.Error("MasterSite not available - no sites to enumerate");
                return list;
            }

            // The site walk needs the master Item (FQID has no GetChildren). If that lookup fails we
            // still run single-site against the master FQID directly.
            Item masterItem = null;
            try { masterItem = Configuration.Instance.GetItem(masterFqid); }
            catch (Exception ex) { Log.Error("GetItem(MasterSite) failed", ex); }

            if (masterItem == null)
            {
                if (masterFqid.ServerId != null)
                    list.Add(new SiteRef { Name = masterFqid.ServerId.ServerHostname, Fqid = masterFqid, ServerId = masterFqid.ServerId });
                return list;
            }

            CollectSites(masterItem, list, new HashSet<Guid>(), 0);
            return list;
        }

        // Distinct sites are kept by ServerId: a site Item's GetChildren returns its federated child
        // sites (each with its own ServerId), while content items share the parent's ServerId and are
        // skipped by the "seen" set - so no Kind filter is needed (there is no Kind.Site).
        private void CollectSites(Item site, List<SiteRef> acc, HashSet<Guid> seen, int depth)
        {
            var fqid = site?.FQID;
            if (fqid?.ServerId == null || depth > 8) return;
            var sid = fqid.ServerId;
            if (!seen.Add(sid.Id)) return;
            acc.Add(new SiteRef
            {
                Name = string.IsNullOrWhiteSpace(site.Name) ? sid.ServerHostname : site.Name,
                Fqid = fqid,
                ServerId = sid
            });

            bool hasKids;
            try { hasKids = site.HasChildren != HasChildren.No; } catch { hasKids = true; }
            if (!hasKids) return;

            List<Item> kids = null;
            try { kids = site.GetChildren(); }
            catch (Exception ex) { Log.Error($"GetChildren failed for site '{site.Name}'", ex); }
            if (kids == null) return;
            foreach (var k in kids)
                CollectSites(k, acc, seen, depth + 1);
        }

        private List<SiteRef> SnapshotSites()
        {
            lock (_lock) { return _sites; }
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

                // The site (federated child or master) whose Event Server sent this response. Each
                // site replaces only its own bucket, so responses from different sites accumulate.
                Guid siteKey = Guid.Empty;
                try { siteKey = message?.ExternalMessageSourceEndPoint?.ServerId?.Id ?? Guid.Empty; } catch { }
                var siteName = SiteNameByServerId(siteKey);

                var users = groups.Values
                    .OrderBy(g => g.User, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(g => g.Type, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => new UserRow
                    {
                        DisplayName = g.User,
                        Secondary = g.Count > 1 ? $"{g.Type} (x{g.Count})" : g.Type,
                        SiteName = siteName
                    })
                    .ToList();

                Log.Info($"WhoAreOnline ({(string.IsNullOrEmpty(siteName) ? "?" : siteName)}): {rawObjs.Count} endpoint(s) -> {users.Count} user/client row(s)");

                lock (_lock)
                {
                    _usersBySite[siteKey] = users;
                    _connectedKeysBySite[siteKey] = connected;
                    RecombineUsersLocked();
                }
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

                var sites = SnapshotSites();
                var map = new Dictionary<Guid, string>();
                var recMap = new Dictionary<Guid, Uri>();
                var recorders = new Dictionary<Uri, string>();
                var recorderSite = new Dictionary<Uri, SiteRef>();
                var cameraSite = new Dictionary<Guid, string>();

                // Authoritative path: enumerate every recording server (and its cameras) from the
                // Management Server configuration of EACH site (master + federated children). This is
                // independent of the device-tree scoping that was only surfacing one recorder. Fall
                // back to the device-tree walk only if no site's config yielded anything (older sites
                // / restricted config access / a recorder-less parent with no readable children).
                int viaConfigSites = 0;
                foreach (var site in sites)
                    if (LoadFromConfig(site, map, recMap, recorders, recorderSite, cameraSite))
                        viaConfigSites++;

                if (viaConfigSites == 0 || map.Count == 0)
                    LoadFromDeviceTree(map, recMap);   // login-scoped fallback (no per-site labels)

                // Best-effort: capture each camera's device-tree folder path for the "Folder" grouping.
                // Independent of the camera source above; cameras the walk misses just have no path.
                var folders = new Dictionary<Guid, string>();
                try { BuildFolderMap(folders); }
                catch (Exception ex) { Log.Error("Folder map build failed", ex); }

                lock (_lock)
                {
                    _enabledCameras = map; _cameraRecorderUri = recMap;
                    _recorders = recorders; _recorderSite = recorderSite;
                    _cameraSiteName = cameraSite; _cameraFolder = folders;
                }
                sw.Stop();
                Log.Info($"Loaded {map.Count} enabled camera(s) across {recorders.Count} recorder(s) / " +
                         $"{sites.Count} site(s) (config sites: {viaConfigSites}); {folders.Count} folder path(s) " +
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
        private bool LoadFromConfig(SiteRef site, Dictionary<Guid, string> map, Dictionary<Guid, Uri> recMap,
                                    Dictionary<Uri, string> recorders, Dictionary<Uri, SiteRef> recorderSite,
                                    Dictionary<Guid, string> cameraSite)
        {
            if (site?.Fqid == null) return false;
            try
            {
                var management = new ManagementServer(site.Fqid);
                int rsCount = 0;
                foreach (var rs in management.RecordingServerFolder.RecordingServers)
                {
                    // Per-recorder isolation: a single offline / half-configured recording server
                    // (whose property or hardware/camera access throws) must not abort enumeration
                    // of the others, nor discard the recorders already collected. Without this, one
                    // bad recorder forced a full fall back to the login-scoped device-tree walk,
                    // which surfaced fewer recording servers than actually exist.
                    try
                    {
                        if (rs == null || !rs.Enabled) continue;
                        var baseUri = RecorderBaseUri(rs);
                        if (baseUri == null) { Log.Info($"[{site.Name}] Recording server '{rs.Name}' has no usable web Uri - skipped"); continue; }
                        rsCount++;
                        if (!recorders.ContainsKey(baseUri))
                            recorders[baseUri] = string.IsNullOrEmpty(rs.Name) ? baseUri.Host : rs.Name;
                        if (!recorderSite.ContainsKey(baseUri))
                            recorderSite[baseUri] = site;

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
                                cameraSite[id] = site.Name;
                                cams++;
                            }
                        }
                        Log.Info($"[{site.Name}] Recording server '{rs.Name}' @ {baseUri} : {cams} enabled camera(s)");
                    }
                    catch (Exception rex)
                    {
                        string name = "?";
                        try { name = rs?.Name ?? "?"; } catch { }
                        // Fold the exception text into the message: the SDK's exception-array
                        // overload does not surface in MIPLog at this level, so the bare reason
                        // would otherwise be lost.
                        var reason = rex.Message;
                        if (rex.InnerException != null) reason += " -> " + rex.InnerException.Message;
                        Log.Error($"[{site.Name}] Recording server '{name}' enumeration failed - skipped: {rex.GetType().Name}: {reason}", rex);
                    }
                }
                return rsCount > 0;
            }
            catch (Exception ex)
            {
                var reason = ex.Message;
                if (ex.InnerException != null) reason += " -> " + ex.InnerException.Message;
                Log.Error($"[{site.Name}] Config API recorder enumeration failed: {ex.GetType().Name}: {reason}", ex);
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
                var sites = SnapshotSites();
                bool federated = sites.Count > 1;
                var list = new List<RoleInfo>();

                foreach (var site in sites)
                {
                    if (site?.Fqid == null) continue;
                    try
                    {
                        var ms = new ManagementServer(site.Fqid);
                        var serverId = site.ServerId;
                        foreach (var role in ms.RoleFolder.Roles)
                        {
                            if (role == null) continue;
                            var roleName = string.IsNullOrWhiteSpace(role.DisplayName) ? role.Name : role.DisplayName;
                            // On a federated system the same role name can exist on several sites, so
                            // qualify it with the site to keep the view-item rows distinct.
                            var info = new RoleInfo { Name = federated ? $"{site.Name} / {roleName}" : roleName };
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
                            catch (Exception ex) { Log.Error($"[{site.Name}] Role members failed for '{info.Name}'", ex); }
                            list.Add(info);
                        }
                    }
                    catch (Exception ex) { Log.Error($"[{site.Name}] LoadRoles (site) failed", ex); }
                }

                lock (_lock) { _roles = list; }
                sw.Stop();
                Log.Info($"Loaded {list.Count} role(s) across {sites.Count} site(s), {list.Sum(r => r.Total)} member assignment(s) in {sw.ElapsedMilliseconds} ms");
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

        // Rebuild the combined user list and connected-key set from the per-site buckets. Caller holds _lock.
        private void RecombineUsersLocked()
        {
            _users = _usersBySite.Values
                .SelectMany(x => x)
                .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(u => u.SiteName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in _connectedKeysBySite.Values) keys.UnionWith(set);
            _connectedUserKeys = keys;
        }

        private string SiteNameByServerId(Guid id)
        {
            if (id == Guid.Empty) return "";
            foreach (var s in _sites)
                if (s.ServerId != null && s.ServerId.Id == id) return s.Name;
            return "";
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
                        _cameraSiteName.TryGetValue(kv.Key, out var siteName);
                        return new CameraRow
                        {
                            Id = kv.Key,
                            Name = kv.Value,
                            Online = string.Equals(state, StateResponding, StringComparison.OrdinalIgnoreCase),
                            SiteName = siteName ?? ""
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
