using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoExporter.Background;
using AutoExporter.Messaging;
using Xunit;

namespace AutoExporter.Tests
{
    public class ExecutionLogTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public ExecutionLogTests()
        {
            _dir  = Path.Combine(Path.GetTempPath(), "autoexport-tests-log-" + Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_dir, "executions.jsonl");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ─── Helpers ────────────────────────────────────────

        private static ExecutionRecord MakeRecord(string jobName, DateTime startedUtc, bool success = true, Guid? jobId = null)
            => new ExecutionRecord
            {
                RunId         = Guid.NewGuid(),
                JobObjectId   = jobId ?? Guid.NewGuid(),
                JobName       = jobName,
                StartedUtc    = startedUtc,
                FinishedUtc   = startedUtc.AddSeconds(10),
                RangeStartUtc = startedUtc.AddDays(-1),
                RangeEndUtc   = startedUtc,
                Format        = "XProtect",
                Trigger       = "Rule",
                Success       = success,
                Error         = success ? "" : "boom",
                CameraCount   = 3,
                BytesWritten  = 12345,
                OutputFolder  = @"D:\test\folder",
                CameraNames   = new List<string> { "Cam A", "Cam B" }
            };

        // ─── Serialize / Deserialize round trip ─────────────

        [Fact]
        public void RoundTrip_preserves_all_fields()
        {
            var r = MakeRecord("Nightly Lobby", new DateTime(2026, 5, 28, 3, 0, 0, DateTimeKind.Utc));

            var serialized = ExecutionLog.Serialize(r);
            var back = ExecutionLog.Deserialize(serialized);

            Assert.Equal(r.RunId, back.RunId);
            Assert.Equal(r.JobObjectId, back.JobObjectId);
            Assert.Equal(r.JobName, back.JobName);
            Assert.Equal(r.StartedUtc, back.StartedUtc);
            Assert.Equal(r.FinishedUtc, back.FinishedUtc);
            Assert.Equal(r.RangeStartUtc, back.RangeStartUtc);
            Assert.Equal(r.RangeEndUtc, back.RangeEndUtc);
            Assert.Equal(r.Format, back.Format);
            Assert.Equal(r.Trigger, back.Trigger);
            Assert.Equal(r.Success, back.Success);
            Assert.Equal(r.Error, back.Error);
            Assert.Equal(r.CameraCount, back.CameraCount);
            Assert.Equal(r.BytesWritten, back.BytesWritten);
            Assert.Equal(r.OutputFolder, back.OutputFolder);
            Assert.Equal(r.CameraNames, back.CameraNames);
        }

        [Fact]
        public void RoundTrip_handles_failure_record()
        {
            var r = MakeRecord("Failing job", DateTime.UtcNow, success: false);
            r.Error = "disk full at \\\\nas\\share";

            var back = ExecutionLog.Deserialize(ExecutionLog.Serialize(r));

            Assert.False(back.Success);
            Assert.Equal(r.Error, back.Error);
        }

        [Fact]
        public void RoundTrip_escapes_special_chars_in_name()
        {
            var r = MakeRecord("Job with \"quotes\" and \\ backslash", DateTime.UtcNow);

            var back = ExecutionLog.Deserialize(ExecutionLog.Serialize(r));

            Assert.Equal(r.JobName, back.JobName);
        }

        [Fact]
        public void RoundTrip_empty_camera_names_list_survives()
        {
            var r = MakeRecord("J", DateTime.UtcNow);
            r.CameraNames = new List<string>();

            var back = ExecutionLog.Deserialize(ExecutionLog.Serialize(r));

            Assert.NotNull(back.CameraNames);
            Assert.Empty(back.CameraNames);
        }

        // ─── Append / LoadRecent ────────────────────────────

        [Fact]
        public void Append_creates_file_and_LoadRecent_returns_record()
        {
            var log = new ExecutionLog(_path);
            log.Append(MakeRecord("J", DateTime.UtcNow));

            Assert.True(File.Exists(_path));
            var loaded = log.LoadRecent();
            Assert.Single(loaded);
            Assert.Equal("J", loaded[0].JobName);
        }

        [Fact]
        public void LoadRecent_returns_empty_when_file_missing()
        {
            var log = new ExecutionLog(Path.Combine(_dir, "missing.jsonl"));
            Assert.Empty(log.LoadRecent());
        }

        // ─── Retention: per-job cap ─────────────────────────

        [Fact]
        public void PruneLines_keeps_newest_MaxPerJob_per_job()
        {
            var jobA = Guid.NewGuid();
            var jobB = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var lines = new List<string>();
            // Job A: 5 records, oldest first
            for (int i = 0; i < 5; i++)
                lines.Add(ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-100 + i), jobId: jobA)));
            // Job B: 5 records, oldest first
            for (int i = 0; i < 5; i++)
                lines.Add(ExecutionLog.Serialize(MakeRecord("B", now.AddMinutes(-50 + i), jobId: jobB)));

            // perJob=3, total=100 → keep newest 3 of each job
            var kept = ExecutionLog.PruneLines(lines, maxPerJob: 3, maxTotal: 100, out var dropped);

            Assert.Equal(4, dropped);   // 2 dropped per job
            Assert.Equal(6, kept.Count);

            var keptRecords = kept.Select(ExecutionLog.Deserialize).ToList();
            Assert.Equal(3, keptRecords.Count(r => r.JobObjectId == jobA));
            Assert.Equal(3, keptRecords.Count(r => r.JobObjectId == jobB));
        }

        [Fact]
        public void PruneLines_drops_oldest_per_job_first()
        {
            var jobA = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var lines = new List<string>
            {
                ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-30), jobId: jobA)),  // oldest
                ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-20), jobId: jobA)),
                ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-10), jobId: jobA)),  // newest
            };

            var kept = ExecutionLog.PruneLines(lines, maxPerJob: 2, maxTotal: 100, out _);
            var records = kept.Select(ExecutionLog.Deserialize).OrderBy(r => r.StartedUtc).ToList();

            Assert.Equal(2, records.Count);
            // The two newest should remain — the -30min one should be gone.
            Assert.DoesNotContain(records, r => Math.Abs((r.StartedUtc - now.AddMinutes(-30)).TotalSeconds) < 1);
        }

        // ─── Retention: total cap ───────────────────────────

        [Fact]
        public void PruneLines_total_cap_applies_after_per_job_cap()
        {
            var now = DateTime.UtcNow;
            var lines = new List<string>();
            // 4 jobs, 10 records each (well under per-job cap of 100), total 40 records.
            for (int j = 0; j < 4; j++)
            {
                var jobId = Guid.NewGuid();
                for (int i = 0; i < 10; i++)
                    lines.Add(ExecutionLog.Serialize(MakeRecord($"J{j}", now.AddMinutes(-100 + j * 10 + i), jobId: jobId)));
            }

            // perJob=100 (no-op per job), total=25 → keep newest 25 globally
            var kept = ExecutionLog.PruneLines(lines, maxPerJob: 100, maxTotal: 25, out var dropped);

            Assert.Equal(15, dropped);
            Assert.Equal(25, kept.Count);
        }

        [Fact]
        public void PruneLines_returns_kept_in_original_file_order()
        {
            var jobA = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var lines = new List<string>();
            // Append in chronological order
            for (int i = 0; i < 5; i++)
                lines.Add(ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-50 + i * 10), jobId: jobA)));

            var kept = ExecutionLog.PruneLines(lines, maxPerJob: 3, maxTotal: 100, out _);
            var records = kept.Select(ExecutionLog.Deserialize).ToList();

            Assert.Equal(3, records.Count);
            for (int i = 1; i < records.Count; i++)
                Assert.True(records[i].StartedUtc > records[i - 1].StartedUtc,
                    "Kept lines should remain in chronological order so future appends stay monotonic");
        }

        [Fact]
        public void PruneLines_noop_when_under_both_caps()
        {
            var jobA = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var lines = new List<string>
            {
                ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-10), jobId: jobA)),
                ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-5),  jobId: jobA)),
            };

            var kept = ExecutionLog.PruneLines(lines, maxPerJob: 100, maxTotal: 1000, out var dropped);

            Assert.Equal(0, dropped);
            Assert.Equal(lines.Count, kept.Count);
        }

        [Fact]
        public void PruneLines_skips_malformed_lines()
        {
            var jobA = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var lines = new List<string>
            {
                "garbage-that-is-not-json",
                ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-5), jobId: jobA)),
                "",
                ExecutionLog.Serialize(MakeRecord("A", now.AddMinutes(-1), jobId: jobA))
            };

            var kept = ExecutionLog.PruneLines(lines, maxPerJob: 100, maxTotal: 100, out _);

            // 2 valid records kept; garbage + blank silently dropped.
            Assert.Equal(2, kept.Count);
        }

        // ─── End-to-end Append rotation ──────────────────────

        [Fact]
        public void Append_rotates_file_when_per_job_cap_exceeded()
        {
            var log = new ExecutionLog(_path);
            var jobA = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // Append 105 records for one job; per-job cap = 100.
            for (int i = 0; i < ExecutionLog.MaxPerJob + 5; i++)
                log.Append(MakeRecord("A", now.AddMinutes(i), jobId: jobA));

            var loaded = log.LoadRecent();
            Assert.Equal(ExecutionLog.MaxPerJob, loaded.Count);
        }

        [Fact]
        public void Append_rotates_file_when_total_cap_exceeded()
        {
            var log = new ExecutionLog(_path);
            var now = DateTime.UtcNow;

            // 50 jobs × 30 records each = 1500 records.
            // Per-job cap = 100 (no-op), total cap = 1000 → final count = 1000.
            // Append 500 newest then check we cap at MaxTotal.
            int total = 0;
            for (int j = 0; j < 50; j++)
            {
                var jobId = Guid.NewGuid();
                for (int i = 0; i < 30; i++)
                    log.Append(MakeRecord($"J{j}", now.AddSeconds(total++), jobId: jobId));
            }

            var loaded = log.LoadRecent();
            // LoadRecent caps at 500 for the UI; the on-disk file should be capped at MaxTotal.
            var onDisk = File.ReadAllLines(_path).Length;
            Assert.True(onDisk <= ExecutionLog.MaxTotal,
                $"on-disk count {onDisk} exceeded MaxTotal {ExecutionLog.MaxTotal}");
        }
    }
}
