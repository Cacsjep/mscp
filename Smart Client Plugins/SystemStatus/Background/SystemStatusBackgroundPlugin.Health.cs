using System;
using System.Collections.Generic;
using System.Linq;
using SystemStatus.Recorder;
using VideoOS.Platform;
using VideoOS.Platform.Data;
using VideoOS.Platform.Login;

namespace SystemStatus.Background
{
    /// <summary>
    /// System-health data pulled from each recording server's RecorderStatusService2, merged with
    /// the live status snapshot (per-camera online state + connected users). Produces the three
    /// tables shown by the health window: recording-server storage, cameras (with live stream
    /// statistics), and users. Recording ranges (first/last recorded timestamp) are queried
    /// separately and on demand via <see cref="FetchRecordingRange"/>, since one SequenceDataSource
    /// per camera does not scale to thousands of cameras up front.
    ///
    /// All SOAP/sequence calls block on the network - call from a worker thread, never the UI.
    /// </summary>
    public partial class SystemStatusBackgroundPlugin
    {
        private const int StatsRequestTimeoutMs = 20000;

        /// <summary>
        /// Query every recording server (that owns at least one enabled camera) for its storage,
        /// attach/connection state and live video statistics, and combine with the current online
        /// state and user list. Never throws: per-recorder failures land in the result's Errors.
        /// </summary>
        public SystemHealthSnapshot FetchSystemHealth()
        {
            var snap = CurrentSnapshot; // live: per-camera online state + users

            Dictionary<Guid, Uri> recorderByCamera;
            lock (_lock) { recorderByCamera = new Dictionary<Guid, Uri>(_cameraRecorderUri); }

            var errors = new List<string>();
            string token = TryGetToken(errors);

            // Group enabled cameras by their recorder so each recorder is queried once.
            var byRecorder = new Dictionary<Uri, List<Guid>>();
            foreach (var kv in recorderByCamera)
            {
                if (!byRecorder.TryGetValue(kv.Value, out var list))
                    byRecorder[kv.Value] = list = new List<Guid>();
                list.Add(kv.Key);
            }

            var storages = new List<StorageRow>();
            var streamsByDevice = new Dictionary<Guid, List<StreamStatRow>>();
            var usedByDevice = new Dictionary<Guid, ulong>();

            // Camera name lookup for the per-stream detail rows.
            var nameById = snap.Cameras.ToDictionary(c => c.Id, c => c.Name);

            if (token != null)
            {
                foreach (var grp in byRecorder)
                {
                    var uri = grp.Key;
                    var host = uri.Host;
                    AttachAndConnectionState state = null;
                    StorageStatus[] rec = null, arc = null;

                    try
                    {
                        var client = new RecorderStatusService2(uri) { Timeout = StatsRequestTimeoutMs };

                        try { state = client.GetRecorderStatus(token); }
                        catch (Exception ex) { Log.Error($"GetRecorderStatus failed for {host}", ex); }

                        try { rec = client.GetRecordingStorageStatus(token); }
                        catch (Exception ex) { errors.Add($"{host} (storage): {Root(ex).Message}"); }

                        try { arc = client.GetArchiveStorageStatus(token); }
                        catch (Exception ex) { Log.Error($"GetArchiveStorageStatus failed for {host}", ex); }

                        var devices = client.GetVideoDeviceStatistics(token, grp.Value.ToArray())
                                      ?? Array.Empty<VideoDeviceStatistics>();
                        foreach (var dev in devices)
                        {
                            if (dev == null) continue;
                            usedByDevice[dev.DeviceId] = dev.UsedSpaceInBytes;
                            nameById.TryGetValue(dev.DeviceId, out var camName);
                            var rows = (dev.VideoStreamStatisticsArray ?? Array.Empty<VideoStreamStatistics>())
                                .Where(s => s != null)
                                .Select(s => BuildStreamRow(camName ?? dev.DeviceId.ToString(), host, s))
                                .ToList();
                            streamsByDevice[dev.DeviceId] = rows;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Recorder status failed for {host}", ex);
                        errors.Add($"{host}: {Root(ex).Message}");
                    }

                    string stateText = FormatState(state);
                    bool ok = IsRecorderOk(state);
                    AddStorageRows(storages, host, stateText, ok, rec, isArchive: false);
                    AddStorageRows(storages, host, stateText, ok, arc, isArchive: true);
                    bool noStorage = (rec == null || rec.Length == 0) && (arc == null || arc.Length == 0);
                    if (noStorage)
                        storages.Add(new StorageRow { RecorderHost = host, State = stateText, RecorderOk = ok, StorageName = "—", Path = "" });
                }
            }

            // Build a camera row for EVERY enabled camera (online or not); attach stream stats and
            // used space where the recorder returned them.
            var cameras = snap.Cameras.Select(c =>
            {
                recorderByCamera.TryGetValue(c.Id, out var u);
                streamsByDevice.TryGetValue(c.Id, out var streams);
                usedByDevice.TryGetValue(c.Id, out var used);
                return new CameraHealthRow
                {
                    Id = c.Id,
                    Name = c.Name,
                    RecorderHost = u?.Host ?? "—",
                    Online = c.Online,
                    UsedSpaceBytes = used,
                    Streams = (IReadOnlyList<StreamStatRow>)streams ?? Array.Empty<StreamStatRow>()
                };
            })
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

            var storageRows = storages
                .OrderBy(s => s.RecorderHost, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.IsArchive)
                .ThenBy(s => s.StorageName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Log.Info($"System health: {storageRows.Count} storage row(s), {cameras.Count} camera(s), " +
                     $"{snap.Users.Count} user(s) from {byRecorder.Count} recorder(s), {errors.Count} error(s)");

            return new SystemHealthSnapshot(storageRows, cameras, snap.Users, errors, byRecorder.Count);
        }

        /// <summary>
        /// Lazily resolve the first and last recorded timestamps for one camera (local time).
        /// Two cheap sequence queries (oldest-forward, newest-backward). Returns Ok=false on error.
        /// </summary>
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
                var epochUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var span = TimeSpan.FromDays(3650);

                // Newest sequence at or before now.
                var newest = src.GetData(nowUtc, span, 1, TimeSpan.Zero, 0,
                                         DataType.SequenceTypeGuids.RecordingSequence);
                DateTime? last = MaxEndLocal(newest);

                // Oldest sequence at or after the epoch.
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
                CameraName = camName,
                RecorderName = recorderHost,
                StreamName = string.IsNullOrWhiteSpace(s.Name) ? "—" : s.Name,
                Width = res?.Width ?? 0,
                Height = res?.Height ?? 0,
                Codec = string.IsNullOrWhiteSpace(s.VideoFormat) ? "—" : s.VideoFormat,
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
                    StorageName = string.IsNullOrWhiteSpace(st.Name) ? "—" : st.Name,
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
