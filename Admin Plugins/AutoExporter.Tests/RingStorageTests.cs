using System;
using System.IO;
using AutoExporter.Background;
using Xunit;

namespace AutoExporter.Tests
{
    public class RingStorageTests : IDisposable
    {
        private readonly string _root;

        public RingStorageTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "autoexport-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        // ─── Helpers ────────────────────────────────────────

        private string MakeRunFolder(string name, DateTime lastWriteUtc, int payloadKB)
        {
            var dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "payload.bin");
            File.WriteAllBytes(file, new byte[payloadKB * 1024]);
            // Set both file and folder mtimes (folder mtime is what RingStorage reads).
            File.SetLastWriteTimeUtc(file, lastWriteUtc);
            Directory.SetLastWriteTimeUtc(dir, lastWriteUtc);
            return dir;
        }

        // ─── Configuration ──────────────────────────────────

        [Fact]
        public void IsConfigured_false_when_no_limits_set()
        {
            Assert.False(new RingStorage(_root, 0, 0).IsConfigured);
        }

        [Fact]
        public void IsConfigured_true_when_any_limit_set()
        {
            Assert.True(new RingStorage(_root, 1024, 0).IsConfigured);
            Assert.True(new RingStorage(_root, 0, 30).IsConfigured);
        }

        [Fact]
        public void FromGigabytes_converts_correctly()
        {
            var ring = RingStorage.FromGigabytes(_root, 2, 7);
            Assert.Equal(2L * 1024 * 1024 * 1024, ring.MaxBytes);
            Assert.Equal(7, ring.MaxAgeDays);
        }

        [Fact]
        public void FromGigabytes_zero_means_unlimited_bytes()
        {
            var ring = RingStorage.FromGigabytes(_root, 0, 7);
            Assert.Equal(0, ring.MaxBytes);
        }

        // ─── Edge cases ─────────────────────────────────────

        [Fact]
        public void Prune_does_nothing_when_root_missing()
        {
            var ring = new RingStorage(Path.Combine(_root, "nope"), 1024, 1);
            var result = ring.Prune();
            Assert.Equal(0, result.PrunedFolders);
            Assert.Equal(0, result.BytesReclaimed);
        }

        [Fact]
        public void Prune_does_nothing_when_root_empty()
        {
            var ring = new RingStorage(_root, 1024, 1);
            var result = ring.Prune();
            Assert.Equal(0, result.PrunedFolders);
        }

        [Fact]
        public void Prune_does_nothing_when_under_both_limits()
        {
            var now = DateTime.UtcNow;
            MakeRunFolder("recent", now.AddDays(-1), 10);

            var ring = new RingStorage(_root, 10L * 1024 * 1024, 30);
            var result = ring.Prune();
            Assert.Equal(0, result.PrunedFolders);
            Assert.True(Directory.Exists(Path.Combine(_root, "recent")));
        }

        // ─── Age-based pruning ──────────────────────────────

        [Fact]
        public void Age_pruning_removes_folders_older_than_cutoff()
        {
            var now = DateTime.UtcNow;
            MakeRunFolder("young", now.AddDays(-1), 10);
            MakeRunFolder("old1",  now.AddDays(-30), 10);
            MakeRunFolder("old2",  now.AddDays(-45), 10);

            var ring = new RingStorage(_root, 0, 7);
            var result = ring.Prune();

            Assert.True(Directory.Exists(Path.Combine(_root, "young")));
            Assert.False(Directory.Exists(Path.Combine(_root, "old1")));
            Assert.False(Directory.Exists(Path.Combine(_root, "old2")));
            Assert.Equal(2, result.PrunedFolders);
            Assert.True(result.BytesReclaimed >= 20 * 1024);
        }

        [Fact]
        public void Age_pruning_keeps_folder_exactly_at_cutoff()
        {
            // mtime just barely inside the cutoff (cutoff itself is *exclusive*)
            var now = DateTime.UtcNow;
            MakeRunFolder("borderline", now.AddDays(-6.9), 5);

            var ring = new RingStorage(_root, 0, 7);
            ring.Prune();

            Assert.True(Directory.Exists(Path.Combine(_root, "borderline")));
        }

        // ─── Size-based pruning (MB scale, direct-byte ctor) ─

        [Fact]
        public void Size_pruning_evicts_oldest_first_until_under_cap()
        {
            var now = DateTime.UtcNow;
            // 4 folders, 1 MB each (= 4 MB total). Cap = 2 MB → evict 2 oldest.
            MakeRunFolder("a_oldest", now.AddHours(-4), 1024);
            MakeRunFolder("b",        now.AddHours(-3), 1024);
            MakeRunFolder("c",        now.AddHours(-2), 1024);
            MakeRunFolder("d_newest", now.AddHours(-1), 1024);

            var ring = new RingStorage(_root, 2L * 1024 * 1024, 0);
            var result = ring.Prune();

            Assert.False(Directory.Exists(Path.Combine(_root, "a_oldest")));
            Assert.False(Directory.Exists(Path.Combine(_root, "b")));
            Assert.True(Directory.Exists(Path.Combine(_root, "c")));
            Assert.True(Directory.Exists(Path.Combine(_root, "d_newest")));
            Assert.Equal(2, result.PrunedFolders);
        }

        [Fact]
        public void Size_pruning_does_not_evict_when_exactly_at_cap()
        {
            var now = DateTime.UtcNow;
            // ~50 KB folder, cap = 1 MB (huge headroom).
            MakeRunFolder("under_cap", now.AddHours(-1), 50);

            var ring = new RingStorage(_root, 1024 * 1024, 0);
            var result = ring.Prune();

            Assert.Equal(0, result.PrunedFolders);
            Assert.True(Directory.Exists(Path.Combine(_root, "under_cap")));
        }

        [Fact]
        public void Combined_age_then_size_pruning()
        {
            var now = DateTime.UtcNow;
            MakeRunFolder("ancient",  now.AddDays(-100), 500);  // killed by age
            MakeRunFolder("oldish",   now.AddHours(-5),  500);  // killed by size next
            MakeRunFolder("medium",   now.AddHours(-3),  500);  // survives
            MakeRunFolder("newest",   now.AddHours(-1),  500);  // survives

            // Age cutoff: 30d → drops "ancient".
            // After age pass: 3 folders × 500KB = 1.5 MB; size cap = 1 MB → drops "oldish".
            var ring = new RingStorage(_root, 1024 * 1024, 30);
            var result = ring.Prune();

            Assert.False(Directory.Exists(Path.Combine(_root, "ancient")));
            Assert.False(Directory.Exists(Path.Combine(_root, "oldish")));
            Assert.True(Directory.Exists(Path.Combine(_root, "medium")));
            Assert.True(Directory.Exists(Path.Combine(_root, "newest")));
            Assert.Equal(2, result.PrunedFolders);
        }

        [Fact]
        public void Only_immediate_children_count_as_run_folders()
        {
            var now = DateTime.UtcNow;
            var ts = now.AddDays(-10);
            var run = MakeRunFolder("run1", ts, 5);

            // Nested subfolder is part of run1's size, not its own run folder.
            var nested = Path.Combine(run, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(nested, "more.bin"), new byte[5 * 1024]);
            // Creating the nested child bumps run1's mtime → re-set it so the age cutoff still applies.
            Directory.SetLastWriteTimeUtc(nested, ts);
            Directory.SetLastWriteTimeUtc(run, ts);

            var ring = new RingStorage(_root, 0, 5);
            var result = ring.Prune();

            Assert.False(Directory.Exists(run));
            // 1 top-level pruned, not 2.
            Assert.Equal(1, result.PrunedFolders);
        }
    }
}
