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

        /// <summary>Bitrate from bytes/sec: kB/s for small, Mbit/s for large.</summary>
        public static string Bitrate(double bytesPerSec)
        {
            if (bytesPerSec <= 0) return "—";
            double mbit = bytesPerSec * 8.0 / 1_000_000.0;
            return mbit >= 1.0
                ? mbit.ToString("#,0.0") + " Mbit/s"
                : (bytesPerSec / 1024.0).ToString("#,0") + " kB/s";
        }
    }

    /// <summary>
    /// One storage (recording or archive) on a recording server, plus the recorder's
    /// attach/connection state. Replaced wholesale on each full refresh (storage changes slowly),
    /// so it does not need change notification.
    /// </summary>
    public sealed class StorageRow
    {
        public string RecorderHost { get; set; }
        public string State { get; set; }          // e.g. "Attached / Connected"
        public bool RecorderOk { get; set; }
        public string StorageName { get; set; }
        public string Path { get; set; }
        public bool IsArchive { get; set; }
        public bool Available { get; set; }
        public ulong UsedBytes { get; set; }
        public ulong FreeBytes { get; set; }

        public string Kind => IsArchive ? "Archive" : "Recording";
        public ulong TotalBytes => UsedBytes + FreeBytes;
        public string Used => ByteFormat.Size(UsedBytes);
        public string Free => ByteFormat.Size(FreeBytes);
        public string Total => ByteFormat.Size(TotalBytes);
        public double UsedPercentValue => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0;
        public string UsedPercent => TotalBytes > 0 ? UsedPercentValue.ToString("0") + " %" : "—";
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
            if (span <= TimeSpan.Zero) return "—";
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
        public bool Online { get; set; }
        public ulong UsedSpaceBytes { get; set; }
        public ulong RecorderTotalBytes { get; set; }
        public ObservableCollection<StreamStatRow> Streams { get; } = new ObservableCollection<StreamStatRow>();

        // ── Aggregates over the streams (parent row) ─────────────────────────
        public int StreamCount => Streams.Count;
        public string StreamCountText => Streams.Count == 0 ? "—" : Streams.Count.ToString();
        public bool HasStreams => Streams.Count > 0;

        public string OnlineText => Online ? "Online" : "Offline";
        public string UsedSpaceText => UsedSpaceBytes > 0 ? ByteFormat.Size(UsedSpaceBytes) : "—";

        public double StoragePercentValue =>
            RecorderTotalBytes > 0 && UsedSpaceBytes > 0 ? (double)UsedSpaceBytes / RecorderTotalBytes * 100.0 : 0;
        public string StoragePercentText =>
            RecorderTotalBytes > 0 && UsedSpaceBytes > 0
                ? (StoragePercentValue >= 1 ? StoragePercentValue.ToString("0.0") : StoragePercentValue.ToString("0.00")) + " %"
                : "—";

        private StreamStatRow Primary => Streams
            .OrderByDescending(s => (long)s.Width * s.Height)
            .ThenByDescending(s => s.Fps)
            .FirstOrDefault();

        public string Resolution => Primary?.Resolution ?? "—";
        public string Codec
        {
            get
            {
                var codecs = Streams.Select(s => s.Codec).Where(c => !string.IsNullOrWhiteSpace(c) && c != "—")
                                    .Distinct().ToList();
                return codecs.Count == 0 ? "—" : string.Join(", ", codecs);
            }
        }
        public string Fps => Primary != null ? Primary.Fps.ToString("0.0") : "—";
        public string Bitrate => Streams.Count == 0 ? "—" : ByteFormat.Bitrate(Streams.Sum(s => (double)s.Bps));

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
            Online = f.Online;
            UsedSpaceBytes = f.UsedSpaceBytes;
            // The lightweight live tick doesn't fetch storage, so it leaves capacity 0 - keep the
            // value cached from the last full refresh rather than wiping the storage-% denominator.
            if (f.RecorderTotalBytes > 0) RecorderTotalBytes = f.RecorderTotalBytes;
            MergeStreams(f.Streams);

            Raise(nameof(Online), nameof(OnlineText), nameof(RecorderHost), nameof(UsedSpaceText),
                  nameof(StoragePercentText), nameof(StreamCountText), nameof(HasStreams),
                  nameof(Resolution), nameof(Codec), nameof(Fps), nameof(Bitrate));
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
                        if (!_firstRecording.HasValue || !_lastRecording.HasValue) return "—";
                        return DurationText.Format(_lastRecording.Value - _firstRecording.Value);
                    default: return "—";
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
                default: return "—";
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
            IReadOnlyList<UserRow> users, IReadOnlyList<string> errors, int recorderCount)
        {
            Storages = storages ?? Array.Empty<StorageRow>();
            Cameras = cameras ?? Array.Empty<CameraHealthRow>();
            Users = users ?? Array.Empty<UserRow>();
            Errors = errors ?? Array.Empty<string>();
            RecorderCount = recorderCount;
        }

        public IReadOnlyList<StorageRow> Storages { get; }
        public IReadOnlyList<CameraHealthRow> Cameras { get; }
        public IReadOnlyList<UserRow> Users { get; }
        public IReadOnlyList<string> Errors { get; }
        public int RecorderCount { get; }

        public int OnlineCameras => Cameras.Count(c => c.Online);
        public int StreamingCameras => Cameras.Count(c => c.HasStreams);

        public string ServersSummary
        {
            get
            {
                ulong used = (ulong)Storages.Aggregate(0m, (a, s) => a + s.UsedBytes);
                return $"{RecorderCount} recorder(s)   •   {Storages.Count} storage(s)   •   {ByteFormat.Size(used)} used";
            }
        }

        public string CamerasSummary =>
            $"{Cameras.Count} camera(s)   •   {OnlineCameras} online   •   {StreamingCameras} streaming";

        public string UsersSummary => $"{Users.Count} connected user(s)";
    }
}
