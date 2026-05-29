using System;
using System.IO;
using AutoExporter.Background;
using Xunit;

namespace AutoExporter.Tests
{
    public class StorageStatusTests : IDisposable
    {
        private readonly string _root;

        public StorageStatusTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "autoexport-status-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private string MakeRunFolder(string name, DateTime mtimeUtc, int sizeKB)
        {
            var dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "payload.bin"), new byte[sizeKB * 1024]);
            Directory.SetLastWriteTimeUtc(dir, mtimeUtc);
            return dir;
        }

        // ─── Health states ──────────────────────────────────

        [Fact]
        public void Empty_path_is_NotConfigured()
        {
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", "", 0, 0);
            Assert.Equal(StorageHealth.NotConfigured, r.Health);
            Assert.False(string.IsNullOrEmpty(r.Detail));
        }

        [Fact]
        public void Whitespace_path_is_NotConfigured()
        {
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", "   ", 0, 0);
            Assert.Equal(StorageHealth.NotConfigured, r.Health);
        }

        [Fact]
        public void Missing_local_folder_is_PathMissing()
        {
            var bogus = Path.Combine(_root, "nope-" + Guid.NewGuid().ToString("N"));
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", bogus, 0, 0);
            Assert.Equal(StorageHealth.PathMissing, r.Health);
            Assert.Contains("not found", r.Detail, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Missing_UNC_is_PathMissing_with_UNC_specific_detail()
        {
            // \\nonexistent.invalid is unreachable and should report the UNC-specific hint.
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", @"\\nonexistent.invalid\share", 0, 0);
            Assert.Equal(StorageHealth.PathMissing, r.Health);
            Assert.Contains("UNC", r.Detail);
        }

        [Fact]
        public void Empty_existing_folder_is_Ok_with_zero_counts()
        {
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", _root, 1024L * 1024 * 1024, 30);
            Assert.Equal(StorageHealth.Ok, r.Health);
            Assert.Equal(0, r.RunFolderCount);
            Assert.Equal(0L, r.UsageBytes);
            Assert.Null(r.OldestRunUtc);
            Assert.Null(r.NewestRunUtc);
        }

        [Fact]
        public void Existing_folder_with_runs_reports_counts_sizes_and_age_bounds()
        {
            var now = DateTime.UtcNow;
            MakeRunFolder("a", now.AddDays(-3), 100);
            MakeRunFolder("b", now.AddDays(-1), 200);
            MakeRunFolder("c", now,             300);

            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", _root, 0, 0);

            Assert.Equal(StorageHealth.Ok, r.Health);
            Assert.Equal(3, r.RunFolderCount);
            Assert.True(r.UsageBytes >= 600 * 1024);
            Assert.NotNull(r.OldestRunUtc);
            Assert.NotNull(r.NewestRunUtc);
            Assert.True(r.OldestRunUtc.Value < r.NewestRunUtc.Value);
        }

        [Fact]
        public void Near_quota_threshold_is_QuotaWarn()
        {
            // 1 MB run; cap = 1.05 MB → ~95% full → QuotaWarn (threshold 90%).
            MakeRunFolder("a", DateTime.UtcNow, 1000);  // ~1 MB
            var cap = (long)(1.05 * 1024 * 1024);
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", _root, cap, 0);
            Assert.Equal(StorageHealth.QuotaWarn, r.Health);
            Assert.NotNull(r.UsagePercentOfMax);
            Assert.InRange(r.UsagePercentOfMax.Value, StorageStatus.QuotaWarnPercent, 99);
        }

        [Fact]
        public void At_or_past_quota_is_QuotaFull()
        {
            // 2 MB run; cap = 1 MB → 200% → QuotaFull.
            MakeRunFolder("a", DateTime.UtcNow, 2 * 1024);
            var cap = 1L * 1024 * 1024;
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", _root, cap, 0);
            Assert.Equal(StorageHealth.QuotaFull, r.Health);
            Assert.Equal(100, r.UsagePercentOfMax);
        }

        [Fact]
        public void Unlimited_quota_has_null_UsagePercentOfMax()
        {
            MakeRunFolder("a", DateTime.UtcNow, 100);
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", _root, 0, 0);
            Assert.Null(r.UsagePercentOfMax);
        }

        [Fact]
        public void Local_path_populates_FreeBytes_and_TotalBytes()
        {
            // Just assert they're not -1 (the "unavailable" sentinel). Actual values vary by box.
            var r = StorageStatus.Inspect(Guid.NewGuid(), "Job", _root, 0, 0);
            Assert.True(r.FreeBytes >= 0, "FreeBytes should be populated for a local path");
            Assert.True(r.TotalBytes >= 0, "TotalBytes should be populated for a local path");
        }

        // ─── UNC detection ──────────────────────────────────

        [Theory]
        [InlineData(@"\\server\share",     true)]
        [InlineData(@"\\server\share\sub", true)]
        [InlineData("//server/share",      true)]
        [InlineData(@"C:\foo",             false)]
        [InlineData("/local/path",         false)]
        [InlineData("",                    false)]
        [InlineData(null,                  false)]
        public void IsUncPath_detection(string path, bool expected)
        {
            Assert.Equal(expected, StorageStatus.IsUncPath(path));
        }
    }
}
