using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            var errors = new List<string>();
            string token = TryGetToken(errors);

            var byRecorder = GroupByRecorder(recorderByCamera);
            // Union in every enumerated recording server (same base Uri the cameras use), so a recorder
            // with no cameras visible to this login still gets its state + storage fetched. The shared
            // Uri keying means recorders that DO own cameras are not fetched twice.
            foreach (var u in SnapshotRecorders().Keys)
                if (!byRecorder.ContainsKey(u)) byRecorder[u] = new List<Guid>();
            var nameById = BuildNameMap(snap);

            var results = token == null
                ? new List<RecorderFetch>()
                : RunPerRecorder(byRecorder, kv => FetchRecorder(token, kv.Key, kv.Value, nameById, includeStorage: true));

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
            var cameras = BuildCameraRows(snap, recorderByCamera, streamsByDevice, usedByDevice, capacityByHost, unreachable);
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

            return new SystemHealthSnapshot(storageRows, cameras, snap.Users, errors, byRecorder.Count);
        }

        // ── Lightweight live fetch (video stats only) ─────────────────────────
        public IReadOnlyList<CameraHealthRow> FetchLiveCameraStats()
        {
            var sw = Stopwatch.StartNew();
            var snap = CurrentSnapshot;
            var recorderByCamera = SnapshotRecorderMap();
            var errors = new List<string>();
            string token = TryGetToken(errors);

            var streamsByDevice = new Dictionary<Guid, List<StreamStatRow>>();
            var usedByDevice = new Dictionary<Guid, ulong>();
            List<RecorderFetch> results = new List<RecorderFetch>();

            if (token != null)
            {
                var byRecorder = GroupByRecorder(recorderByCamera);
                var nameById = BuildNameMap(snap);
                results = RunPerRecorder(byRecorder, kv => FetchRecorder(token, kv.Key, kv.Value, nameById, includeStorage: false));
                foreach (var r in results)
                {
                    foreach (var kv in r.UsedByDevice) usedByDevice[kv.Key] = kv.Value;
                    foreach (var kv in r.StreamsByDevice) streamsByDevice[kv.Key] = kv.Value;
                }
            }

            // capacity null -> RecorderTotalBytes 0, preserved by CameraHealthRow.ApplyLiveFrom.
            var unreachable = UnreachableHosts(results);
            var cameras = BuildCameraRows(snap, recorderByCamera, streamsByDevice, usedByDevice, null, unreachable);

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
                    bool reachable = true;
                    if (includeStorage)
                    {
                        // Probe with the cheapest call first; if it fails the recorder is unreachable,
                        // so skip the remaining storage/stats calls rather than timing out on each.
                        try { r.State = client.GetRecorderStatus(token); }
                        catch (Exception ex) { reachable = false; r.Error = $"{r.Host}: {Root(ex).Message}"; }

                        if (reachable)
                        {
                            try { r.Rec = client.GetRecordingStorageStatus(token); }
                            catch (Exception ex) { r.Error = $"{r.Host} (storage): {Root(ex).Message}"; }
                            try { r.Arc = client.GetArchiveStorageStatus(token); }
                            catch (Exception ex) { Log.Error($"GetArchiveStorageStatus failed for {r.Host}", ex); }
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
                        var devices = client.GetVideoDeviceStatistics(token, ids.ToArray())
                                      ?? Array.Empty<VideoDeviceStatistics>();
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
                Log.Info($"Slow recorder {r.Host}: {r.Ms}ms ({r.DeviceCount} device(s), {ids.Count} requested)");
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

        // ── Recording range (lazy / eager-but-throttled by the window) ────────
        public RecordingRangeResult FetchRecordingRange(Guid cameraId)
        {
            SequenceDataSource src = null;
            try
            {
                var item = Configuration.Instance.GetItem(cameraId, Kind.Camera);
                if (item == null) return RecordingRangeResult.Failed;

                src = new SequenceDataSource(item);
                src.Init();

                var nowUtc = DateTime.UtcNow;
                var epochUtc = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var span = (nowUtc - epochUtc) + TimeSpan.FromDays(1);

                var newest = src.GetData(nowUtc, span, 1, TimeSpan.Zero, 0,
                                         DataType.SequenceTypeGuids.RecordingSequence);
                DateTime? last = MaxEndLocal(newest);

                var oldest = src.GetData(epochUtc, TimeSpan.Zero, 0, span, 1,
                                         DataType.SequenceTypeGuids.RecordingSequence);
                DateTime? first = MinStartLocal(oldest);

                return new RecordingRangeResult { Ok = true, First = first, Last = last };
            }
            catch (Exception ex)
            {
                Log.Error($"FetchRecordingRange failed for {cameraId}", ex);
                return RecordingRangeResult.Failed;
            }
            finally
            {
                try { src?.Close(); } catch { }
            }
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
            HashSet<string> unreachableHosts)
        {
            var rows = new List<CameraHealthRow>(snap.Cameras.Count);
            foreach (var c in snap.Cameras)
            {
                recorderByCamera.TryGetValue(c.Id, out var u);
                var host = u?.Host ?? "-";
                bool reachable = u == null || unreachableHosts == null || !unreachableHosts.Contains(host);
                var row = new CameraHealthRow { Id = c.Id, Name = c.Name, RecorderHost = host, Online = c.Online, RecorderReachable = reachable };
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

        private string TryGetToken(List<string> errors)
        {
            try
            {
                var token = LoginSettingsCache.GetLoginSettings(EnvironmentManager.Instance.CurrentSite)?.Token;
                if (string.IsNullOrEmpty(token)) { errors.Add("No session token available."); return null; }
                return token;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read login token", ex);
                errors.Add("No session token available: " + Root(ex).Message);
                return null;
            }
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
