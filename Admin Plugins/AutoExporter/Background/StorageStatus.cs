using System;
using System.IO;
using System.Linq;

namespace AutoExporter.Background
{
    [Serializable]
    public enum StorageHealth { Ok, NotConfigured, PathMissing, AccessDenied, IOError, QuotaWarn, QuotaFull }

    [Serializable]
    public class StorageStatusReport
    {
        public Guid JobObjectId;
        public string JobName;
        public string StoragePath;
        public StorageHealth Health;
        public string Detail;        // human-readable explanation
        public long UsageBytes;      // sum of run-folder sizes; 0 if unknown
        public long FreeBytes;       // -1 if unavailable (UNC where DriveInfo fails)
        public long TotalBytes;      // -1 if unavailable
        public int  RunFolderCount;
        public long MaxBytes;        // 0 = unlimited
        public int  MaxAgeDays;      // 0 = unlimited
        public DateTime? OldestRunUtc;
        public DateTime? NewestRunUtc;

        public int? UsagePercentOfMax =>
            MaxBytes > 0 ? (int?)Math.Min(100, (int)(UsageBytes * 100L / MaxBytes)) : null;
    }

    /// <summary>
    /// Pure status inspector. Probes a job's storage path and returns a snapshot —
    /// no MIP dependencies, so it's safe to call from the admin UI thread and easy
    /// to unit-test.
    /// </summary>
    internal static class StorageStatus
    {
        public const int QuotaWarnPercent = 90;

        public static StorageStatusReport Inspect(Guid jobObjectId, string jobName, string storagePath, long maxBytes, int maxAgeDays)
        {
            var report = new StorageStatusReport
            {
                JobObjectId = jobObjectId,
                JobName     = jobName ?? "",
                StoragePath = storagePath ?? "",
                MaxBytes    = maxBytes,
                MaxAgeDays  = maxAgeDays,
                FreeBytes   = -1,
                TotalBytes  = -1
            };

            if (string.IsNullOrWhiteSpace(storagePath))
            {
                report.Health = StorageHealth.NotConfigured;
                report.Detail = "No storage path configured";
                return report;
            }

            // Existence check (handles missing local folders and unreachable UNC).
            bool exists;
            try { exists = Directory.Exists(storagePath); }
            catch (Exception ex)
            {
                report.Health = StorageHealth.IOError;
                report.Detail = "Cannot probe path: " + ex.Message;
                return report;
            }

            if (!exists)
            {
                report.Health = StorageHealth.PathMissing;
                report.Detail = IsUncPath(storagePath)
                    ? "UNC share not reachable (or service account lacks access)"
                    : "Folder does not exist";
                return report;
            }

            // Free space — best effort. UNC paths typically fail with DriveInfo.
            try
            {
                var root = Path.GetPathRoot(storagePath);
                if (!string.IsNullOrEmpty(root) && !IsUncPath(storagePath))
                {
                    var di = new DriveInfo(root);
                    if (di.IsReady)
                    {
                        report.FreeBytes  = di.AvailableFreeSpace;
                        report.TotalBytes = di.TotalSize;
                    }
                }
            }
            catch { /* leave FreeBytes/TotalBytes at -1 */ }

            // Enumerate run folders & measure.
            try
            {
                var runFolders = Directory.EnumerateDirectories(storagePath).ToList();
                report.RunFolderCount = runFolders.Count;

                long total = 0;
                DateTime? oldest = null, newest = null;
                foreach (var rf in runFolders)
                {
                    try
                    {
                        total += DirectorySize(rf);
                        var mtime = Directory.GetLastWriteTimeUtc(rf);
                        if (oldest == null || mtime < oldest) oldest = mtime;
                        if (newest == null || mtime > newest) newest = mtime;
                    }
                    catch { /* per-folder errors are non-fatal */ }
                }
                report.UsageBytes    = total;
                report.OldestRunUtc  = oldest;
                report.NewestRunUtc  = newest;
            }
            catch (UnauthorizedAccessException)
            {
                report.Health = StorageHealth.AccessDenied;
                report.Detail = "Access denied to storage folder";
                return report;
            }
            catch (Exception ex)
            {
                report.Health = StorageHealth.IOError;
                report.Detail = "Error reading folder: " + ex.Message;
                return report;
            }

            // Health.
            if (report.UsagePercentOfMax >= 100)
            {
                report.Health = StorageHealth.QuotaFull;
                report.Detail = $"At quota — pruning will evict oldest runs";
            }
            else if (report.UsagePercentOfMax >= QuotaWarnPercent)
            {
                report.Health = StorageHealth.QuotaWarn;
                report.Detail = $"Near quota ({report.UsagePercentOfMax}%)";
            }
            else
            {
                report.Health = StorageHealth.Ok;
                report.Detail = "OK";
            }

            return report;
        }

        internal static bool IsUncPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
        }

        private static long DirectorySize(string path)
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }
    }
}
