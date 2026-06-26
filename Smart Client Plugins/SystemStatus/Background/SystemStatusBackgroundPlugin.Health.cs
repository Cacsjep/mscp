using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SystemStatus.Recorder;
using VideoOS.Platform;
using VideoOS.Platform.Data;
using VideoOS.Platform.Login;

namespace SystemStatus.Background
{
    /// <summary>
    /// System-health data pulled from each recording server's RecorderStatusService2, merged with
    /// the live status snapshot (per-camera online state + connected users). Per-recorder SOAP calls
    /// run in parallel so the fetch stays fast with many recorders. Two entry points:
    ///   - <see cref="FetchSystemHealth"/>: full (storage + state + stats), used on open / manual refresh.
    ///   - <see cref="FetchLiveCameraStats"/>: lightweight (video stats only), used by the 2s live tick.
    /// Recording ranges are queried separately and on demand via <see cref="FetchRecordingRange"/>.
    /// All calls block on the network - invoke from a worker thread, never the UI thread.
    /// </summary>
    public partial class SystemStatusBackgroundPlugin
    {
        // Per-call SOAP timeout. Kept short so an offline recorder surfaces quickly; combined with
        // the short-circuit below (skip the remaining calls once one fails) an offline recorder costs
        // roughly one timeout, not four. Other recorders are unaffected (calls run in parallel).
        private const int StatsRequestTimeoutMs = 3000;
        private const int SlowRecorderMs = 1500;       // log recorders slower than this
        private const int MaxRecorderConcurrency = 12; // parallel recorder SOAP calls

        /// <summary>Per-recorder fetch result (one worker per recorder).</summary>
        private sealed class RecorderFetch
        {
            public string Host;
            public long Ms;
            // Per-SOAP-call timings (ms), for the slow-recorder breakdown in the log.
            public long StatusMs, RecStorageMs, ArcStorageMs, StatsMs;
            public AttachAndConnectionState State;
            public string StateText = "Unknown";
            public bool Ok;
            public StorageStatus[] Rec;
            public StorageStatus[] Arc;
            public readonly Dictionary<Guid, ulong> UsedByDevice = new Dictionary<Guid, ulong>();
            public readonly Dictionary<Guid, List<StreamStatRow>> StreamsByDevice = new Dictionary<Guid, List<StreamStatRow>>();
            public int DeviceCount;
            public int StreamCount;
            public bool StatsOk;   // GetVideoDeviceStatistics succeeded (recorder reachable)
            public string Error;
        }

        // ── Full fetch (storage + state + stats) ──────────────────────────────
        public SystemHealthSnapshot FetchSystemHealth()
        {
            var sw = Stopwatch.StartNew();
            var snap = CurrentSnapshot;
            var recorderByCamera = SnapshotRecorderMap();
            var recSites = SnapshotRecorderSites();
            var errors = new List<string>();

            var byRecorder = GroupByRecorder(recorderByCamera);
            // Union in every enumerated recording server (same base Uri the cameras use), so a recorder
            // with no cameras visible to this login still gets its state + storage fetched. The shared
            // Uri keying means recorders that DO own cameras are not fetched twice.
            foreach (var u in SnapshotRecorders().Keys)
                if (!byRecorder.ContainsKey(u)) byRecorder[u] = new List<Guid>();
            var nameById = BuildNameMap(snap);

            // Each recorder is queried with a token for its own site (federated children included).
            var tokens = BuildRecorderTokens(recSites, byRecorder.Keys, errors);
            var results = tokens.Values.All(string.IsNullOrEmpty)
                ? new List<RecorderFetch>()
                : RunPerRecorder(byRecorder, kv => FetchRecorder(TokenForRecorder(tokens, kv.Key), kv.Key, kv.Value, nameById, includeStorage: true));

            var storages = new List<StorageRow>();
            var streamsByDevice = new Dictionary<Guid, List<StreamStatRow>>();
            var usedByDevice = new Dictionary<Guid, ulong>();
            foreach (var r in results)
            {
                if (r.Error != null) errors.Add(r.Error);
                AddStorageRows(storages, r.Host, r.StateText, r.Ok, r.Rec, isArchive: false);
                AddStorageRows(storages, r.Host, r.StateText, r.Ok, r.Arc, isArchive: true);
                if ((r.Rec == null || r.Rec.Length == 0) && (r.Arc == null || r.Arc.Length == 0))
                    storages.Add(new StorageRow { RecorderHost = r.Host, State = r.StateText, RecorderOk = r.Ok, StorageName = "-", Path = "" });
                foreach (var kv in r.UsedByDevice) usedByDevice[kv.Key] = kv.Value;
                foreach (var kv in r.StreamsByDevice) streamsByDevice[kv.Key] = kv.Value;
            }

            var capacityByHost = storages
                .GroupBy(s => s.RecorderHost, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (ulong)g.Aggregate(0m, (a, s) => a + s.TotalBytes),
                              StringComparer.OrdinalIgnoreCase);

            var unreachable = UnreachableHosts(results);
            var cameras = BuildCameraRows(snap, recorderByCamera, streamsByDevice, usedByDevice, capacityByHost, unreachable, SnapshotFolderMap(), SnapshotCameraSites());

            // Label each storage row with the federated site that owns its recorder.
            var siteNameByHost = SiteNameByHost(recSites);
            foreach (var s in storages)
                s.SiteName = siteNameByHost.TryGetValue(s.RecorderHost, out var sn) ? sn : "";

            var storageRows = storages
                .OrderBy(s => s.RecorderHost, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.IsArchive)
                .ThenBy(s => s.StorageName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            sw.Stop();
            var slow = results.OrderByDescending(r => r.Ms).FirstOrDefault();
            Log.Info($"FetchSystemHealth: {byRecorder.Count} recorder(s), {cameras.Count} camera(s), " +
                     $"{storageRows.Count} storage(s), {results.Sum(r => r.StreamCount)} stream(s) in {sw.ElapsedMilliseconds}ms; " +
                     $"slowest {slow?.Host}={slow?.Ms ?? 0}ms; errors={errors.Count}");
            foreach (var r in results)
                Log.Info($"  recorder {r.Host}: {r.Ms}ms, {r.DeviceCount} device(s), {r.StreamCount} stream(s)" +
                         (r.Error != null ? " ERR " + r.Error : ""));

            return new SystemHealthSnapshot(storageRows, cameras, snap.Users, errors, byRecorder.Count, SnapshotSites().Count);
        }

        // ── Lightweight live fetch (video stats only) ─────────────────────────
        public IReadOnlyList<CameraHealthRow> FetchLiveCameraStats()
        {
            var sw = Stopwatch.StartNew();
            var snap = CurrentSnapshot;
            var recorderByCamera = SnapshotRecorderMap();
            var recSites = SnapshotRecorderSites();
            var errors = new List<string>();

            var streamsByDevice = new Dictionary<Guid, List<StreamStatRow>>();
            var usedByDevice = new Dictionary<Guid, ulong>();
            List<RecorderFetch> results = new List<RecorderFetch>();

            var byRecorder = GroupByRecorder(recorderByCamera);
            var tokens = BuildRecorderTokens(recSites, byRecorder.Keys, errors);
            if (!tokens.Values.All(string.IsNullOrEmpty))
            {
                var nameById = BuildNameMap(snap);
                results = RunPerRecorder(byRecorder, kv => FetchRecorder(TokenForRecorder(tokens, kv.Key), kv.Key, kv.Value, nameById, includeStorage: false));
                foreach (var r in results)
                {
                    foreach (var kv in r.UsedByDevice) usedByDevice[kv.Key] = kv.Value;
                    foreach (var kv in r.StreamsByDevice) streamsByDevice[kv.Key] = kv.Value;
                }
            }

            // capacity null -> RecorderTotalBytes 0, preserved by CameraHealthRow.ApplyLiveFrom.
            var unreachable = UnreachableHosts(results);
            var cameras = BuildCameraRows(snap, recorderByCamera, streamsByDevice, usedByDevice, null, unreachable, SnapshotFolderMap(), SnapshotCameraSites());

            sw.Stop();
            int errCount = results.Count(r => r.Error != null);
            // The periodic full fetch logs full metrics every ~10s; for the frequent light tick only
            // log when it's notably slow or has errors, to keep the MIP log readable at scale.
            if (sw.ElapsedMilliseconds > 750 || errCount > 0)
                Log.Info($"Live tick: {results.Count} recorder(s), {results.Sum(r => r.StreamCount)} stream(s), " +
                         $"{cameras.Count} camera(s) in {sw.ElapsedMilliseconds}ms; errors={errCount}");
            return cameras;
        }

        // ── Per-recorder worker ───────────────────────────────────────────────
        private RecorderFetch FetchRecorder(string token, Uri uri, List<Guid> ids,
                                            Dictionary<Guid, string> nameById, bool includeStorage)
        {
            var r = new RecorderFetch { Host = uri.Host };
            var sw = Stopwatch.StartNew();
            try
            {
                using (var client = new RecorderStatusService2(uri) { Timeout = StatsRequestTimeoutMs })
                {
                    var swCall = Stopwatch.StartNew();
                    bool reachable = true;
                    if (includeStorage)
                    {
                        // Probe with the cheapest call first; if it fails the recorder is unreachable,
                        // so skip the remaining storage/stats calls rather than timing out on each.
                        swCall.Restart();
                        try { r.State = client.GetRecorderStatus(token); }
                        catch (Exception ex) { reachable = false; r.Error = $"{r.Host}: {Root(ex).Message}"; }
                        r.StatusMs = swCall.ElapsedMilliseconds;

                        if (reachable)
                        {
                            swCall.Restart();
                            try { r.Rec = client.GetRecordingStorageStatus(token); }
                            catch (Exception ex) { r.Error = $"{r.Host} (storage): {Root(ex).Message}"; }
                            r.RecStorageMs = swCall.ElapsedMilliseconds;

                            swCall.Restart();
                            try { r.Arc = client.GetArchiveStorageStatus(token); }
                            catch (Exception ex) { Log.Error($"GetArchiveStorageStatus failed for {r.Host}", ex); }
                            r.ArcStorageMs = swCall.ElapsedMilliseconds;
                        }
                    }

                    if (reachable && ids.Count == 0)
                    {
                        // Recorder enumerated from config but with no cameras visible to this login -
                        // the storage/state above is all we need; skip the empty stats call.
                        r.StatsOk = true;
                    }
                    else if (reachable)
                    {
                        swCall.Restart();
                        var devices = client.GetVideoDeviceStatistics(token, ids.ToArray())
                                      ?? Array.Empty<VideoDeviceStatistics>();
                        r.StatsMs = swCall.ElapsedMilliseconds;
                        r.DeviceCount = devices.Length;
                        foreach (var dev in devices)
                        {
                            if (dev == null) continue;
                            r.UsedByDevice[dev.DeviceId] = dev.UsedSpaceInBytes;
                            nameById.TryGetValue(dev.DeviceId, out var camName);
                            var rows = (dev.VideoStreamStatisticsArray ?? Array.Empty<VideoStreamStatistics>())
                                .Where(s => s != null)
                                .Select(s => BuildStreamRow(camName ?? dev.DeviceId.ToString(), r.Host, s))
                                .ToList();
                            r.StreamsByDevice[dev.DeviceId] = rows;
                            r.StreamCount += rows.Count;
                        }
                        r.StatsOk = true; // GetVideoDeviceStatistics returned => recorder reachable
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Recorder fetch failed for {r.Host}", ex);
                if (r.Error == null) r.Error = $"{r.Host}: {Root(ex).Message}";
            }
            sw.Stop();
            r.Ms = sw.ElapsedMilliseconds;
            r.StateText = FormatState(r.State);
            r.Ok = IsRecorderOk(r.State);
            if (r.Ms >= SlowRecorderMs)
                Log.Info($"Slow recorder {r.Host}: {r.Ms}ms ({r.DeviceCount} device(s), {ids.Count} requested) " +
                         $"[status={r.StatusMs}ms recStorage={r.RecStorageMs}ms arcStorage={r.ArcStorageMs}ms stats={r.StatsMs}ms]");
            return r;
        }

        private List<RecorderFetch> RunPerRecorder(Dictionary<Uri, List<Guid>> byRecorder,
                                                   Func<KeyValuePair<Uri, List<Guid>>, RecorderFetch> worker)
        {
            if (byRecorder.Count == 0) return new List<RecorderFetch>();
            var bag = new ConcurrentBag<RecorderFetch>();
            var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(MaxRecorderConcurrency, byRecorder.Count)) };
            try
            {
                Parallel.ForEach(byRecorder, opts, kv =>
                {
                    try { bag.Add(worker(kv)); }
                    catch (Exception ex) { bag.Add(new RecorderFetch { Host = kv.Key.Host, Error = $"{kv.Key.Host}: {Root(ex).Message}" }); }
                });
            }
            catch (Exception ex) { Log.Error("RunPerRecorder failed", ex); }
            return bag.ToList();
        }

        // ── Background recording-range pre-warm ───────────────────────────────
        // Walked once per session, starting at Smart Client launch, at a gentle concurrency so the
        // recorders are never stormed (the cause of the SequencesGetAroundWithSpan 60s timeouts on
        // large systems). The health window reads the cache via GetCameraRange and shows ranges that
        // are already loaded immediately, "pending" for ones still in flight, and "error" for failures.
        private const int RangePrewarmConcurrency = 2;     // gentle: never storm the recorders
        private const int RangeRescanMinutes = 30;          // re-walk all spans this often to stay current

        private sealed class RangeCacheEntry { public bool Done; public bool Ok; public DateTime? First; public DateTime? Last; }
        private readonly ConcurrentDictionary<Guid, RangeCacheEntry> _rangeCache = new ConcurrentDictionary<Guid, RangeCacheEntry>();
        private CancellationTokenSource _rangeSweepCts;

        private void StartRangePrewarm()
        {
            _rangeSweepCts = new CancellationTokenSource();
            var token = _rangeSweepCts.Token;
            Task.Factory.StartNew(() => RangeSweepLoop(token), token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void StopRangePrewarm()
        {
            try { _rangeSweepCts?.Cancel(); } catch { }
        }

        // Phase 1: load the first/last recording for every camera not cached yet (initial warm, plus
        // any cameras a later config change added). Phase 2: once everything is cached, idle for
        // RangeRescanMinutes, then re-walk ALL cameras to keep last-recording / span current as the
        // recordings grow - keeping the previous value visible until the fresh one arrives. Repeat.
        private void RangeSweepLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !_closing)
                {
                    LoadMissingRanges(token);
                    if (token.IsCancellationRequested || _closing) break;

                    if (token.WaitHandle.WaitOne(TimeSpan.FromMinutes(RangeRescanMinutes))) break;
                    RefreshAllRanges(token);
                }
            }
            catch (Exception ex) { Log.Error("Range pre-warm loop failed", ex); }
        }

        // Load only cameras with no cache entry yet (marks them "pending" so the window shows "…").
        private void LoadMissingRanges(CancellationToken token)
        {
            List<Guid> todo;
            lock (_lock) { todo = _enabledCameras.Keys.Where(id => !_rangeCache.ContainsKey(id)).ToList(); }
            if (todo.Count == 0) return;

            Log.Info($"Range pre-warm: walking {todo.Count} camera range(s) at concurrency {RangePrewarmConcurrency}");
            var sw = Stopwatch.StartNew();
            RunRangeBatch(todo, token, markPending: true);
            sw.Stop();
            Log.Info($"Range pre-warm: pass complete in {sw.ElapsedMilliseconds} ms ({_rangeCache.Count} camera(s) cached)");
        }

        // Re-query every enabled camera, replacing each cache entry only when the fresh value is ready
        // (markPending: false), so the table keeps showing the prior span during the rescan.
        private void RefreshAllRanges(CancellationToken token)
        {
            List<Guid> all;
            lock (_lock) { all = _enabledCameras.Keys.ToList(); }
            if (all.Count == 0) return;

            Log.Info($"Range rescan: refreshing {all.Count} camera range(s)");
            var sw = Stopwatch.StartNew();
            RunRangeBatch(all, token, markPending: false);
            sw.Stop();
            Log.Info($"Range rescan: complete in {sw.ElapsedMilliseconds} ms");
        }

        private void RunRangeBatch(List<Guid> ids, CancellationToken token, bool markPending)
        {
            var times = new ConcurrentBag<long>();   // per-camera wall time (ms)
            int ok = 0, fail = 0;
            long slowest = 0; Guid slowestId = Guid.Empty;
            var slowLock = new object();

            using (var sem = new SemaphoreSlim(RangePrewarmConcurrency))
            {
                var tasks = ids.Select(id => Task.Run(() =>
                {
                    try { sem.Wait(token); } catch { return; }
                    try
                    {
                        if (token.IsCancellationRequested || _closing) return;
                        if (markPending) _rangeCache.TryAdd(id, new RangeCacheEntry { Done = false });

                        var swCam = Stopwatch.StartNew();
                        var res = FetchRecordingRange(id);
                        swCam.Stop();

                        times.Add(swCam.ElapsedMilliseconds);
                        if (res != null && res.Ok) Interlocked.Increment(ref ok); else Interlocked.Increment(ref fail);
                        lock (slowLock) { if (swCam.ElapsedMilliseconds > slowest) { slowest = swCam.ElapsedMilliseconds; slowestId = id; } }

                        _rangeCache[id] = (res != null && res.Ok)
                            ? new RangeCacheEntry { Done = true, Ok = true, First = res.First, Last = res.Last }
                            : new RangeCacheEntry { Done = true, Ok = false };
                    }
                    finally { try { sem.Release(); } catch { } }
                }, token)).ToArray();
                try { Task.WaitAll(tasks, token); } catch { /* cancelled */ }
            }

            LogRangeBatchStats(times.ToList(), ok, fail, slowest, slowestId);
        }

        // Rich per-batch timing summary: count, ok/fail, min/avg/max and the 95th percentile of the
        // per-camera range-query wall time, plus the single slowest camera. Enough to spot a slow
        // recorder or a pathological camera without per-camera spam.
        private void LogRangeBatchStats(List<long> times, int ok, int fail, long slowest, Guid slowestId)
        {
            if (times.Count == 0) return;
            times.Sort();
            long min = times[0];
            long max = times[times.Count - 1];
            double avg = times.Average();
            long p95 = times[(int)Math.Min(times.Count - 1, Math.Floor(times.Count * 0.95))];
            Log.Info($"Range timing: {times.Count} queried (ok={ok}, fail={fail}); " +
                     $"min={min}ms avg={avg:0}ms p95={p95}ms max={max}ms; slowest={CameraLabel(slowestId)} {slowest}ms");
        }

        /// <summary>The cached recording range for a camera (Pending until the background walk reaches it).</summary>
        public CameraRange GetCameraRange(Guid cameraId)
        {
            if (_rangeCache.TryGetValue(cameraId, out var e) && e.Done)
                return e.Ok
                    ? new CameraRange { State = RangeLoadState.Loaded, First = e.First, Last = e.Last }
                    : new CameraRange { State = RangeLoadState.Failed };
            return new CameraRange { State = RangeLoadState.Pending };
        }

        // Recording-range queries slower than this are logged individually (with the first/last
        // breakdown) so a problem camera or recorder is easy to spot in MIPLog.
        private const int SlowRangeMs = 2000;

        // ── Recording range (single lookup used by the background pre-warm) ───
        // The two SequencesGetAroundWithSpan calls are timed separately: "last" (newest) is cheap,
        // "first" (oldest, scanning back to 1990 across archives) is the one that times out on large
        // systems - the per-phase timing makes which one is hurting obvious.
        public RecordingRangeResult FetchRecordingRange(Guid cameraId)
        {
            SequenceDataSource src = null;
            var swTotal = Stopwatch.StartNew();
            long lastMs = 0, firstMs = 0;
            string phase = "init";
            try
            {
                var item = Configuration.Instance.GetItem(cameraId, Kind.Camera);
                if (item == null) return RecordingRangeResult.Failed;

                src = new SequenceDataSource(item);
                src.Init();

                var nowUtc = DateTime.UtcNow;
                var epochUtc = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var span = (nowUtc - epochUtc) + TimeSpan.FromDays(1);

                phase = "last";
                var swCall = Stopwatch.StartNew();
                var newest = src.GetData(nowUtc, span, 1, TimeSpan.Zero, 0,
                                         DataType.SequenceTypeGuids.RecordingSequence);
                lastMs = swCall.ElapsedMilliseconds;
                DateTime? last = MaxEndLocal(newest);

                phase = "first";
                swCall.Restart();
                var oldest = src.GetData(epochUtc, TimeSpan.Zero, 0, span, 1,
                                         DataType.SequenceTypeGuids.RecordingSequence);
                firstMs = swCall.ElapsedMilliseconds;
                DateTime? first = MinStartLocal(oldest);

                swTotal.Stop();
                if (swTotal.ElapsedMilliseconds >= SlowRangeMs)
                    Log.Info($"Range query slow: {CameraLabel(cameraId)} took {swTotal.ElapsedMilliseconds}ms " +
                             $"(last={lastMs}ms, first={firstMs}ms)");
                return new RecordingRangeResult { Ok = true, First = first, Last = last };
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                var root = Root(ex);
                bool timeout = root is TimeoutException
                    || (root.Message?.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0);
                Log.Error($"Range query {(timeout ? "TIMEOUT" : "failed")}: {CameraLabel(cameraId)} phase={phase} " +
                          $"after {swTotal.ElapsedMilliseconds}ms (last={lastMs}ms, first={firstMs}ms): " +
                          $"{root.GetType().Name}: {root.Message}");
                return RecordingRangeResult.Failed;
            }
            finally
            {
                try { src?.Close(); } catch { }
            }
        }

        private string CameraLabel(Guid id)
        {
            string name = null;
            lock (_lock) { _enabledCameras.TryGetValue(id, out name); }
            var shortId = id.ToString().Substring(0, 8);
            return string.IsNullOrEmpty(name) ? shortId : $"{name} ({shortId})";
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private Dictionary<Guid, Uri> SnapshotRecorderMap()
        {
            lock (_lock) { return new Dictionary<Guid, Uri>(_cameraRecorderUri); }
        }

        private Dictionary<Uri, string> SnapshotRecorders()
        {
            lock (_lock) { return new Dictionary<Uri, string>(_recorders); }
        }

        private Dictionary<Uri, SiteRef> SnapshotRecorderSites()
        {
            lock (_lock) { return new Dictionary<Uri, SiteRef>(_recorderSite); }
        }

        private Dictionary<Guid, string> SnapshotCameraSites()
        {
            lock (_lock) { return new Dictionary<Guid, string>(_cameraSiteName); }
        }

        private Dictionary<Guid, string> SnapshotFolderMap()
        {
            lock (_lock) { return new Dictionary<Guid, string>(_cameraFolder); }
        }

        private static Dictionary<Uri, List<Guid>> GroupByRecorder(Dictionary<Guid, Uri> recorderByCamera)
        {
            var byRecorder = new Dictionary<Uri, List<Guid>>();
            foreach (var kv in recorderByCamera)
            {
                if (!byRecorder.TryGetValue(kv.Value, out var list))
                    byRecorder[kv.Value] = list = new List<Guid>();
                list.Add(kv.Key);
            }
            return byRecorder;
        }

        private static Dictionary<Guid, string> BuildNameMap(StatusSnapshot snap)
        {
            var map = new Dictionary<Guid, string>();
            foreach (var c in snap.Cameras) map[c.Id] = c.Name;
            return map;
        }

        // Recorders whose stats call did not return (offline / unreachable), by host.
        private static HashSet<string> UnreachableHosts(List<RecorderFetch> results)
        {
            return new HashSet<string>(
                results.Where(r => !r.StatsOk && r.Host != null).Select(r => r.Host),
                StringComparer.OrdinalIgnoreCase);
        }

        private static List<CameraHealthRow> BuildCameraRows(StatusSnapshot snap,
            Dictionary<Guid, Uri> recorderByCamera,
            Dictionary<Guid, List<StreamStatRow>> streamsByDevice,
            Dictionary<Guid, ulong> usedByDevice,
            Dictionary<string, ulong> capacityByHost,
            HashSet<string> unreachableHosts,
            Dictionary<Guid, string> folderByCamera,
            Dictionary<Guid, string> siteByCamera)
        {
            var rows = new List<CameraHealthRow>(snap.Cameras.Count);
            foreach (var c in snap.Cameras)
            {
                recorderByCamera.TryGetValue(c.Id, out var u);
                var host = u?.Host ?? "-";
                bool reachable = u == null || unreachableHosts == null || !unreachableHosts.Contains(host);
                string folder = null;
                folderByCamera?.TryGetValue(c.Id, out folder);
                // Prefer the camera's own site (from the WhoAreOnline/config map); fall back to the
                // recorder's site for cameras discovered via the device-tree path.
                string site = c.SiteName;
                if (string.IsNullOrEmpty(site)) siteByCamera?.TryGetValue(c.Id, out site);
                var row = new CameraHealthRow { Id = c.Id, Name = c.Name, RecorderHost = host, SiteName = site ?? "", FolderPath = folder, Online = c.Online, RecorderReachable = reachable };
                if (usedByDevice != null && usedByDevice.TryGetValue(c.Id, out var used)) row.UsedSpaceBytes = used;
                if (capacityByHost != null && capacityByHost.TryGetValue(host, out var cap)) row.RecorderTotalBytes = cap;
                if (streamsByDevice != null && streamsByDevice.TryGetValue(c.Id, out var streams) && streams != null)
                    foreach (var s in streams) row.Streams.Add(s);
                rows.Add(row);
            }
            return rows
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        // Per-recorder session token: each recorder's status service must be called with a token for
        // the recorder's own site, so federated child-site recorders authenticate correctly. Recorders
        // discovered via the device-tree fallback (no site mapping) use the current site's token,
        // matching the previous single-site behaviour.
        private Dictionary<Uri, string> BuildRecorderTokens(Dictionary<Uri, SiteRef> recSites,
                                                            IEnumerable<Uri> recorderUris, List<string> errors)
        {
            var tokenBySite = new Dictionary<Guid, string>();
            var fallback = SafeToken(EnvironmentManager.Instance.CurrentSite);
            var result = new Dictionary<Uri, string>();
            foreach (var u in recorderUris)
            {
                string t = null;
                if (recSites != null && recSites.TryGetValue(u, out var site) && site?.Fqid != null && site.ServerId != null)
                {
                    var key = site.ServerId.Id;
                    if (!tokenBySite.TryGetValue(key, out t)) { t = SafeToken(site.Fqid); tokenBySite[key] = t; }
                }
                result[u] = string.IsNullOrEmpty(t) ? fallback : t;
            }
            if (result.Count > 0 && result.Values.All(string.IsNullOrEmpty))
                errors.Add("No session token available.");
            return result;
        }

        private static string TokenForRecorder(Dictionary<Uri, string> tokens, Uri u)
            => tokens != null && tokens.TryGetValue(u, out var t) ? t : null;

        private static string SafeToken(FQID site)
        {
            try { return site == null ? null : LoginSettingsCache.GetLoginSettings(site)?.Token; }
            catch { return null; }
        }

        // Recorder host -> site name, for labelling storage rows by federated site.
        private static Dictionary<string, string> SiteNameByHost(Dictionary<Uri, SiteRef> recSites)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (recSites == null) return map;
            foreach (var kv in recSites)
            {
                var h = kv.Key?.Host;
                if (!string.IsNullOrEmpty(h) && !map.ContainsKey(h)) map[h] = kv.Value?.Name ?? "";
            }
            return map;
        }

        private static StreamStatRow BuildStreamRow(string camName, string recorderHost, VideoStreamStatistics s)
        {
            var res = s.ImageResolution; // reference type, may be null on the wire
            return new StreamStatRow
            {
                StreamId = s.StreamId,
                CameraName = camName,
                RecorderName = recorderHost,
                StreamName = string.IsNullOrWhiteSpace(s.Name) ? "-" : s.Name,
                Width = res?.Width ?? 0,
                Height = res?.Height ?? 0,
                Codec = string.IsNullOrWhiteSpace(s.VideoFormat) ? "-" : s.VideoFormat,
                Fps = s.FPS,
                FpsRequested = s.FPSRequested,
                Bps = s.BPS,
                FrameSizeBytes = s.ImageSizeInBytes,
                Recording = s.RecordingStream,
                Live = s.LiveStream
            };
        }

        private static void AddStorageRows(List<StorageRow> into, string host, string stateText, bool ok,
                                           StorageStatus[] storages, bool isArchive)
        {
            if (storages == null) return;
            foreach (var st in storages)
            {
                if (st == null) continue;
                into.Add(new StorageRow
                {
                    RecorderHost = host,
                    State = stateText,
                    RecorderOk = ok,
                    StorageId = st.StorageId,
                    StorageName = string.IsNullOrWhiteSpace(st.Name) ? "-" : st.Name,
                    Path = st.Path ?? "",
                    IsArchive = isArchive,
                    Available = st.Available,
                    UsedBytes = st.UsedSpaceInBytes,
                    FreeBytes = st.FreeSpaceInBytes
                });
            }
        }

        private static string FormatState(AttachAndConnectionState s)
        {
            if (s == null) return "Unknown";
            var attach = string.IsNullOrWhiteSpace(s.AttachState) ? "?" : s.AttachState;
            var conn = string.IsNullOrWhiteSpace(s.ConnectionState) ? "?" : s.ConnectionState;
            return $"{attach} / {conn}";
        }

        private static bool IsRecorderOk(AttachAndConnectionState s)
        {
            if (s == null) return false;
            return (s.ConnectionState?.IndexOf("Connected", StringComparison.OrdinalIgnoreCase) >= 0)
                && (s.AttachState?.IndexOf("Attached", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static DateTime? MaxEndLocal(IEnumerable<object> data)
        {
            DateTime? best = null;
            if (data == null) return null;
            foreach (var o in data)
            {
                if (!(o is SequenceData sd) || sd.EventSequence == null) continue;
                var e = ToLocal(sd.EventSequence.EndDateTime);
                if (best == null || e > best.Value) best = e;
            }
            return best;
        }

        private static DateTime? MinStartLocal(IEnumerable<object> data)
        {
            DateTime? best = null;
            if (data == null) return null;
            foreach (var o in data)
            {
                if (!(o is SequenceData sd) || sd.EventSequence == null) continue;
                var s = ToLocal(sd.EventSequence.StartDateTime);
                if (best == null || s < best.Value) best = s;
            }
            return best;
        }

        private static DateTime ToLocal(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Unspecified) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return utc.ToLocalTime();
        }

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
