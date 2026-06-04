using System;
using System.Collections.Generic;
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
    /// attach/connection state. One row per storage; a recorder with no reported storage
    /// still gets a single state-only row.
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
        public string AvailableText => Available ? "Yes" : "No";
    }

    /// <summary>
    /// One enabled camera with its online state, live stream statistics (if the recorder is
    /// currently producing them), used recording space, and lazily-loaded first/last recording
    /// timestamps. Per-stream rows live in <see cref="Streams"/> for the expandable detail panel.
    /// Implements INotifyPropertyChanged so the recording-range cells update after their on-demand
    /// load completes.
    /// </summary>
    public sealed class CameraHealthRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public Guid Id { get; set; }
        public string Name { get; set; }
        public string RecorderHost { get; set; }
        public bool Online { get; set; }
        public ulong UsedSpaceBytes { get; set; }
        public IReadOnlyList<StreamStatRow> Streams { get; set; } = Array.Empty<StreamStatRow>();

        // ── Aggregates over the streams (parent row) ─────────────────────────
        public int StreamCount => Streams.Count;
        public string StreamCountText => Streams.Count == 0 ? "—" : Streams.Count.ToString();
        public bool HasStreams => Streams.Count > 0;

        public string OnlineText => Online ? "Online" : "Offline";
        public string UsedSpaceText => UsedSpaceBytes > 0 ? ByteFormat.Size(UsedSpaceBytes) : "—";

        // Largest stream by pixel count drives the headline resolution/codec/fps.
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

        // ── Lazy recording range ─────────────────────────────────────────────
        private RangeState _rangeState = RangeState.NotLoaded;
        private DateTime? _firstRecording;
        private DateTime? _lastRecording;

        public enum RangeState { NotLoaded, Loading, Loaded, Failed }

        public RangeState RangeStatus
        {
            get => _rangeState;
            set { _rangeState = value; Raise(nameof(FirstRecordingText)); Raise(nameof(LastRecordingText)); }
        }

        public void SetRange(DateTime? first, DateTime? last)
        {
            _firstRecording = first;
            _lastRecording = last;
            _rangeState = RangeState.Loaded;
            Raise(nameof(FirstRecordingText));
            Raise(nameof(LastRecordingText));
        }

        public string FirstRecordingText => RangeText(_firstRecording);
        public string LastRecordingText => RangeText(_lastRecording);

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

        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
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
    /// Immutable result of one system-health fetch: storages, cameras and users, plus any
    /// per-recorder errors. Built off the live status snapshot (online state + users) merged
    /// with the recorder-status SOAP results.
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
