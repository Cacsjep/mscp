using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunitySDK;

namespace AutoExporter.Background
{
    /// <summary>
    /// Enforces per-job MAX GB / MAX TIME limits on a single storage root by
    /// deleting the oldest top-level run folders first. Run folders are the
    /// immediate children of the root (one folder per export).
    /// </summary>
    internal class RingStorage
    {
        private static readonly PluginLog _log = new PluginLog("AutoExporter.RingStorage");

        public string Root { get; }
        public long MaxBytes { get; }
        public int MaxAgeDays { get; }

        public RingStorage(string root, long maxBytes, int maxAgeDays)
        {
            Root = root ?? "";
            MaxBytes = maxBytes > 0 ? maxBytes : 0;
            MaxAgeDays = maxAgeDays;
        }

        public static RingStorage FromGigabytes(string root, long maxGB, int maxAgeDays)
            => new RingStorage(root, maxGB > 0 ? maxGB * 1024L * 1024L * 1024L : 0, maxAgeDays);

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Root) && (MaxBytes > 0 || MaxAgeDays > 0);

        /// <summary>
        /// Returns the number of folders pruned and bytes reclaimed.
        /// </summary>
        public CleanupResult Prune()
        {
            var result = new CleanupResult();
            if (string.IsNullOrWhiteSpace(Root) || !Directory.Exists(Root))
                return result;

            List<RunFolder> folders;
            try
            {
                folders = EnumerateRunFolders(Root);
            }
            catch (Exception ex)
            {
                _log.Error($"Enumerate failed at '{Root}': {ex.Message}");
                return result;
            }

            // 1. Age-based pruning.
            if (MaxAgeDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-MaxAgeDays);
                foreach (var f in folders.Where(x => x.LastWriteUtc < cutoff).ToList())
                {
                    if (TryDelete(f.Path, f.Size))
                    {
                        result.PrunedFolders++;
                        result.BytesReclaimed += f.Size;
                        folders.Remove(f);
                    }
                }
            }

            // 2. Size-based pruning (oldest first).
            if (MaxBytes > 0)
            {
                long total = folders.Sum(f => f.Size);
                if (total > MaxBytes)
                {
                    foreach (var f in folders.OrderBy(x => x.LastWriteUtc))
                    {
                        if (total <= MaxBytes) break;
                        if (TryDelete(f.Path, f.Size))
                        {
                            result.PrunedFolders++;
                            result.BytesReclaimed += f.Size;
                            total -= f.Size;
                        }
                    }
                }
            }

            if (result.PrunedFolders > 0)
                _log.Info($"Pruned {result.PrunedFolders} run folder(s), reclaimed {result.BytesReclaimed / (1024 * 1024)} MB");

            return result;
        }

        private static List<RunFolder> EnumerateRunFolders(string root)
        {
            var list = new List<RunFolder>();
            foreach (var runDir in Directory.EnumerateDirectories(root))
            {
                DateTime mtime;
                long size;
                try
                {
                    mtime = Directory.GetLastWriteTimeUtc(runDir);
                    size = DirectorySize(runDir);
                }
                catch (Exception ex)
                {
                    _log.Error($"Stat failed for '{runDir}': {ex.Message}");
                    continue;
                }
                list.Add(new RunFolder { Path = runDir, LastWriteUtc = mtime, Size = size });
            }
            return list;
        }

        private static long DirectorySize(string path)
        {
            long total = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
            return total;
        }

        private static bool TryDelete(string path, long size)
        {
            try
            {
                Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Delete failed for '{path}' ({size / (1024 * 1024)} MB): {ex.Message}");
                return false;
            }
        }

        private class RunFolder
        {
            public string Path;
            public DateTime LastWriteUtc;
            public long Size;
        }
    }

    internal class CleanupResult
    {
        public int PrunedFolders;
        public long BytesReclaimed;
    }
}
