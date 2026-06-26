using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SystemStatus
{
    /// <summary>Byte-size formatting shared by the health rows.</summary>
    internal static class ByteFormat
    {
        public static string Size(ulong bytes)
        {
            if (bytes == 0) return "0";
            double b = bytes;
            string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (i >= 3 ? b.ToString("#,0.0") : b.ToString("#,0")) + " " + u[i];
        }

        /// <summary>
        /// Bitrate in kB/s (kilobytes per second), matching what the recorder reports (BPS is
        /// bytes/sec) and what the Smart Client video-diagnostics overlay shows. Used for both the
        /// per-stream value and the per-camera aggregate so the two columns are directly comparable
        /// (a single-stream camera reads identically to its stream).
        /// </summary>
        public static string Bitrate(double bytesPerSec)
        {
            if (bytesPerSec <= 0) return "-";
            return (bytesPerSec / 1024.0).ToString("#,0") + " kB/s";
        }

        /// <summary>
        /// Byte-rate that scales its unit with magnitude (kB/s, MB/s, GB/s, TB/s), keeping the same
        /// byte base as <see cref="Bitrate"/> so it stays comparable with the per-camera figures.
        /// Used for the recorder-aggregate bandwidth, which can run into MB/s or GB/s.
        /// </summary>
        public static string BitrateScaled(double bytesPerSec)
        {
            if (bytesPerSec <= 0) return "-";
            double v = bytesPerSec / 1024.0; // start in kB/s
            string[] u = { "kB/s", "MB/s", "GB/s", "TB/s" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("#,0") : v.ToString("#,0.0")) + " " + u[i];
        }
    }

    /// <summary>
    /// One storage (recording or archive) on a recording server, plus the recorder's
    /// attach/connection state. Live values are updated in place via <see cref="ApplyFrom"/> so the
    /// servers table keeps its sort/selection across refreshes. Identity = host + StorageId + kind.
    /// </summary>
    public sealed class StorageRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string RecorderHost { get; set; }
        // The federated site this recorder belongs to (empty on a non-federated system).
        public string SiteName { get; set; }
        public string State { get; set; }          // e.g. "Attached / Connected"
        public bool RecorderOk { get; set; }
        public Guid StorageId { get; set; }
        public string StorageName { get; set; }
        public string Path { get; set; }
        public bool IsArchive { get; set; }
        public bool Available { get; set; }
        public ulong UsedBytes { get; set; }
        public ulong FreeBytes { get; set; }

        public string Key => $"{RecorderHost}|{StorageId}|{IsArchive}";

        /// <summary>Update live values from a freshly-fetched row with the same Key, and notify.</summary>
        public void ApplyFrom(StorageRow f)
        {
            State = f.State; RecorderOk = f.RecorderOk; Available = f.Available;
            Path = f.Path; StorageName = f.StorageName;
            UsedBytes = f.UsedBytes; FreeBytes = f.FreeBytes;
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var n in new[] { nameof(State), nameof(RecorderOk), nameof(AvailableText), nameof(Path),
                nameof(StorageName), nameof(Used), nameof(Free), nameof(Total), nameof(UsedPercent),
                nameof(UsedPercentValue), nameof(UsageTooltip) })
                h(this, new PropertyChangedEventArgs(n));
        }

        // Total live bandwidth of all cameras on this recorder (bytes/sec). Computed in the health
        // window from the camera rows (which know their RecorderHost) and pushed onto every storage
        // row of the recorder, so each recorder's table row shows its aggregate throughput.
        private double _recorderBps;
        public double RecorderBandwidthValue
        {
            get => _recorderBps;
            set
            {
                if (_recorderBps == value) return;
                _recorderBps = value;
                var h = PropertyChanged;
                if (h == null) return;
                h(this, new PropertyChangedEventArgs(nameof(RecorderBandwidthValue)));
                h(this, new PropertyChangedEventArgs(nameof(RecorderBandwidthText)));
            }
        }
        public string RecorderBandwidthText => _recorderBps > 0 ? ByteFormat.BitrateScaled(_recorderBps) : "-";

        // Online / total camera counts for this recorder (e.g. "69/72"), computed in the health window
        // and pushed onto every storage row of the recorder.
        private int _camOnline, _camTotal;
        public void SetRecorderCameraCounts(int online, int total)
        {
            if (_camOnline == online && _camTotal == total) return;
            _camOnline = online; _camTotal = total;
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var n in new[] { nameof(RecorderCamerasText), nameof(RecorderCamerasOnlineText),
                nameof(RecorderCamerasTotalText), nameof(RecorderCamerasTotalValue) })
                h(this, new PropertyChangedEventArgs(n));
        }
        public string RecorderCamerasText => _camTotal > 0 ? $"{_camOnline}/{_camTotal}" : "-";
        // Split parts so the table can show the online count in green and "/total" in accent blue.
        public string RecorderCamerasOnlineText => _camTotal > 0 ? _camOnline.ToString() : "-";
        public string RecorderCamerasTotalText => _camTotal > 0 ? "/" + _camTotal : "";
        public int RecorderCamerasTotalValue => _camTotal;

        public string Kind => IsArchive ? "Archive" : "Recording";
        public ulong TotalBytes => UsedBytes + FreeBytes;
        public string Used => ByteFormat.Size(UsedBytes);
        public string Free => ByteFormat.Size(FreeBytes);
        public string Total => ByteFormat.Size(TotalBytes);
        public double UsedPercentValue => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0;
        public string UsedPercent => TotalBytes > 0 ? UsedPercentValue.ToString("0") + " %" : "-";
        public bool HasTotal => TotalBytes > 0;
        public string UsageTooltip => TotalBytes > 0
            ? $"Used {Used} of {Total}  ({Free} free)"
            : "No storage figures reported";
        public string AvailableText => Available ? "Yes" : "No";
    }

    /// <summary>Compact human duration: decimal days (no trailing ".0"), falling to hours/minutes.</summary>
    internal static class DurationText
    {
        public static string Format(TimeSpan span)
        {
            if (span <= TimeSpan.Zero) return "-";
            if (span.TotalDays >= 1) return Trim(span.TotalDays) + " Days";
            if (span.TotalHours >= 1) return Trim(span.TotalHours) + " Hours";
            return Math.Max(1, Math.Round(span.TotalMinutes)).ToString("0") + " Minutes";
        }

        // 90.0 -> "90", 89.94 -> "89.9", 5.62 -> "5.6".
        private static string Trim(double v)
        {
            double r = Math.Round(v, 1);
            return r == Math.Floor(r) ? r.ToString("0") : r.ToString("0.0");
        }
    }

    /// <summary>Result of an on-demand recording-range lookup (local time).</summary>
    public sealed class RecordingRangeResult
    {
        public static readonly RecordingRangeResult Failed = new RecordingRangeResult { Ok = false };
        public bool Ok { get; set; }
        public DateTime? First { get; set; }
        public DateTime? Last { get; set; }
    }

    /// <summary>State of a camera's recording range in the background pre-warm cache.</summary>
    public enum RangeLoadState { Pending, Loaded, Failed }

    /// <summary>
    /// A camera's first/last recording as held in the background cache. The health window reads this
    /// on open / refresh: <see cref="RangeLoadState.Pending"/> = still being walked in the background,
    /// <see cref="RangeLoadState.Loaded"/> = values ready, <see cref="RangeLoadState.Failed"/> = the
    /// query failed (e.g. recorder timeout).
    /// </summary>
    public struct CameraRange
    {
        public RangeLoadState State;
        public DateTime? First;
        public DateTime? Last;
    }

    /// <summary>
    /// One enabled camera with online state, live stream statistics, used recording space, the
    /// camera's share of its recorder's configured storage, and lazily-loaded first/last recording
    /// timestamps. Live values are updated in place via <see cref="ApplyLiveFrom"/> on each refresh
    /// (preserving selection/scroll/sort and the already-loaded recording range). All mutation must
    /// happen on the UI thread because <see cref="Streams"/> is an ObservableCollection.
    /// </summary>
    public sealed class CameraHealthRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public Guid Id { get; set; }
        public string Name { get; set; }
        public string RecorderHost { get; set; }
        // The federated site the camera belongs to (empty on a non-federated system). Drives the
        // optional "Site" column / grouping in the health window.
        public string SiteName { get; set; }
        public string SiteGroup => string.IsNullOrEmpty(SiteName) ? "(local)" : SiteName;
        // Device-tree folder path from the Management Client system hierarchy, e.g.
        // "video-hq-rec1 / Building A". Drives the optional "Folder" grouping in the health window.
        // Null when the camera wasn't reached by the device-tree walk (then it groups under "(no folder)").
        public string FolderPath { get; set; }
        public string FolderGroup => string.IsNullOrEmpty(FolderPath) ? "(no folder)" : FolderPath;
        public bool Online { get; set; }
        // False when the owning recording server didn't answer (offline / unreachable). The camera's
        // real online state is then unknown, so the UI shows it gray rather than green/red.
        public bool RecorderReachable { get; set; } = true;
        public ulong UsedSpaceBytes { get; set; }
        public ulong RecorderTotalBytes { get; set; }
        public ObservableCollection<StreamStatRow> Streams { get; } = new ObservableCollection<StreamStatRow>();

        // ── Aggregates over the streams (parent row) ─────────────────────────
        public int StreamCount => Streams.Count;
        public string StreamCountText => Streams.Count == 0 ? "-" : Streams.Count.ToString();
        public bool HasStreams => Streams.Count > 0;

        public string OnlineText => !RecorderReachable ? "Unknown" : (Online ? "Online" : "Offline");
        // "Unreachable" (gray) | "Online" (green) | "Offline" (red) - drives the status dot.
        public string ConnectivityState => !RecorderReachable ? "Unreachable" : (Online ? "Online" : "Offline");
        public string UsedSpaceText => UsedSpaceBytes > 0 ? ByteFormat.Size(UsedSpaceBytes) : "-";

        public double StoragePercentValue =>
            RecorderTotalBytes > 0 && UsedSpaceBytes > 0 ? (double)UsedSpaceBytes / RecorderTotalBytes * 100.0 : 0;
        public string StoragePercentText =>
            RecorderTotalBytes > 0 && UsedSpaceBytes > 0
                ? (StoragePercentValue >= 1 ? StoragePercentValue.ToString("0.0") : StoragePercentValue.ToString("0.00")) + " %"
                : "-";

        private StreamStatRow Primary => Streams
            .OrderByDescending(s => (long)s.Width * s.Height)
            .ThenByDescending(s => s.Fps)
            .FirstOrDefault();

        public string Resolution => Primary?.Resolution ?? "-";
        public string Codec
        {
            get
            {
                var codecs = Streams.Select(s => s.Codec).Where(c => !string.IsNullOrWhiteSpace(c) && c != "-")
                                    .Distinct().ToList();
                return codecs.Count == 0 ? "-" : string.Join(", ", codecs);
            }
        }
        public string Fps => Primary != null ? Primary.Fps.ToString("0.0") : "-";
        public string Bitrate => Streams.Count == 0 ? "-" : ByteFormat.BitrateScaled(Streams.Sum(s => (double)s.Bps));

        // Numeric backing values for DataGrid column sorting (SortMemberPath).
        public double BitrateValue => Streams.Sum(s => (double)s.Bps);
        public double FpsValue => Primary?.Fps ?? 0;
        public long PixelValue => Primary != null ? (long)Primary.Width * Primary.Height : 0;

        /// <summary>
        /// Update live fields from a freshly-fetched row (same Id), merging streams in place. Does
        /// NOT touch the recording range. UI thread only.
        /// </summary>
        public void ApplyLiveFrom(CameraHealthRow f)
        {
            RecorderHost = f.RecorderHost;
            SiteName = f.SiteName;
            FolderPath = f.FolderPath;
            Online = f.Online;
            RecorderReachable = f.RecorderReachable;
            UsedSpaceBytes = f.UsedSpaceBytes;
            // The lightweight live tick doesn't fetch storage, so it leaves capacity 0 - keep the
            // value cached from the last full refresh rather than wiping the storage-% denominator.
            if (f.RecorderTotalBytes > 0) RecorderTotalBytes = f.RecorderTotalBytes;
            MergeStreams(f.Streams);

            Raise(nameof(Online), nameof(OnlineText), nameof(ConnectivityState), nameof(RecorderReachable),
                  nameof(RecorderHost), nameof(SiteName), nameof(SiteGroup), nameof(FolderGroup), nameof(UsedSpaceText), nameof(StoragePercentText),
                  nameof(StreamCountText), nameof(HasStreams), nameof(Resolution), nameof(Codec), nameof(Fps), nameof(Bitrate));
        }

        private void MergeStreams(IEnumerable<StreamStatRow> fresh)
        {
            var freshList = fresh as IList<StreamStatRow> ?? fresh.ToList();
            var byId = Streams.ToDictionary(s => s.StreamId);
            var seen = new HashSet<Guid>();

            foreach (var f in freshList)
            {
                seen.Add(f.StreamId);
                if (byId.TryGetValue(f.StreamId, out var existing)) existing.ApplyFrom(f);
                else Streams.Add(f);
            }
            for (int i = Streams.Count - 1; i >= 0; i--)
                if (!seen.Contains(Streams[i].StreamId)) Streams.RemoveAt(i);
        }

        // ── Lazy recording range ─────────────────────────────────────────────
        private RangeState _rangeState = RangeState.NotLoaded;
        private DateTime? _firstRecording;
        private DateTime? _lastRecording;

        public enum RangeState { NotLoaded, Loading, Loaded, Failed }
        public RangeState RangeStatus
        {
            get => _rangeState;
            set { _rangeState = value; Raise(nameof(FirstRecordingText), nameof(LastRecordingText), nameof(SpanText)); }
        }

        public void SetRange(DateTime? first, DateTime? last)
        {
            _firstRecording = first;
            _lastRecording = last;
            _rangeState = RangeState.Loaded;
            Raise(nameof(FirstRecordingText), nameof(LastRecordingText), nameof(SpanText));
        }

        public string FirstRecordingText => RangeText(_firstRecording);
        public string LastRecordingText => RangeText(_lastRecording);

        /// <summary>Recording coverage span (last minus first), formatted compactly.</summary>
        public string SpanText
        {
            get
            {
                switch (_rangeState)
                {
                    case RangeState.Loading: return "…";
                    case RangeState.Failed: return "error";
                    case RangeState.Loaded:
                        if (!_firstRecording.HasValue || !_lastRecording.HasValue) return "-";
                        return DurationText.Format(_lastRecording.Value - _firstRecording.Value);
                    default: return "-";
                }
            }
        }

        public double SpanDaysValue =>
            _rangeState == RangeState.Loaded && _firstRecording.HasValue && _lastRecording.HasValue
            && _lastRecording.Value > _firstRecording.Value
                ? (_lastRecording.Value - _firstRecording.Value).TotalDays : 0;

        private string RangeText(DateTime? dt)
        {
            switch (_rangeState)
            {
                case RangeState.Loading: return "…";
                case RangeState.Failed: return "error";
                case RangeState.Loaded: return dt.HasValue ? dt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "(none)";
                default: return "-";
            }
        }

        private void Raise(params string[] names)
        {
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var n in names) h(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>
    /// Immutable result of one full system-health fetch: storages, cameras and users, plus any
    /// per-recorder errors and timing metrics for diagnostics.
    /// </summary>
    public sealed class SystemHealthSnapshot
    {
        public SystemHealthSnapshot(IReadOnlyList<StorageRow> storages, IReadOnlyList<CameraHealthRow> cameras,
            IReadOnlyList<UserRow> users, IReadOnlyList<string> errors, int recorderCount, int siteCount)
        {
            Storages = storages ?? Array.Empty<StorageRow>();
            Cameras = cameras ?? Array.Empty<CameraHealthRow>();
            Users = users ?? Array.Empty<UserRow>();
            Errors = errors ?? Array.Empty<string>();
            RecorderCount = recorderCount;
            SiteCount = siteCount;
        }

        public IReadOnlyList<StorageRow> Storages { get; }
        public IReadOnlyList<CameraHealthRow> Cameras { get; }
        public IReadOnlyList<UserRow> Users { get; }
        public IReadOnlyList<string> Errors { get; }
        public int RecorderCount { get; }
        // Number of federated sites contributing to this snapshot. 1 on a standalone system; the
        // health window shows its Site column/grouping only when this is greater than 1.
        public int SiteCount { get; }
    }
}
