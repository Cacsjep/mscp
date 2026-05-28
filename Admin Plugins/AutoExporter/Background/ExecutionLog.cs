using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AutoExporter.Messaging;
using CommunitySDK;

namespace AutoExporter.Background
{
    /// <summary>
    /// Append-only JSONL log of every export run. Lives at a fixed path under
    /// <c>%ProgramData%\MSCPlugins\AutoExporter\executions.jsonl</c> so the Executions
    /// admin view doesn't depend on any specific job's storage path. The path can be
    /// overridden via the constructor for testing.
    /// </summary>
    internal class ExecutionLog
    {
        // UI shows at most this many.
        private const int MaxRecordsLoaded = 500;

        // Retention caps enforced on every append.
        // After per-job pruning, the global cap is the final bound (in case of many jobs).
        internal const int MaxPerJob = 100;
        internal const int MaxTotal  = 1000;

        private static readonly PluginLog _log = new PluginLog("AutoExporter.ExecutionLog");
        private readonly object _writeLock = new object();

        public string FullPath { get; }

        public ExecutionLog() : this(DefaultPath()) { }

        public ExecutionLog(string fullPath)
        {
            FullPath = fullPath;
        }

        private static string DefaultPath()
        {
            var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(pd, "MSCPlugins", "AutoExporter", "executions.jsonl");
        }

        public void Append(ExecutionRecord record)
        {
            if (string.IsNullOrWhiteSpace(FullPath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FullPath));
                lock (_writeLock)
                {
                    using (var fs = new FileStream(FullPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                    {
                        sw.WriteLine(Serialize(record));
                    }
                    EnforceRetention();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Append failed at '{FullPath}': {ex.Message}");
            }
        }

        // Caller must hold _writeLock.
        // Enforces MaxPerJob (oldest entries dropped per JobObjectId) then MaxTotal
        // (oldest dropped globally). Writes the file back if any line was dropped.
        private void EnforceRetention()
        {
            try
            {
                if (!File.Exists(FullPath)) return;
                var lines = File.ReadAllLines(FullPath);
                if (lines.Length == 0) return;

                var keptLines = PruneLines(lines, MaxPerJob, MaxTotal, out int droppedCount);
                if (droppedCount == 0) return;

                var tmp = FullPath + ".tmp";
                File.WriteAllLines(tmp, keptLines, new UTF8Encoding(false));
                File.Delete(FullPath);
                File.Move(tmp, FullPath);

                _log.Info($"Execution log pruned: dropped {droppedCount} entries (per-job={MaxPerJob}, total={MaxTotal})");
            }
            catch (Exception ex)
            {
                _log.Error($"Retention enforcement failed at '{FullPath}': {ex.Message}");
            }
        }

        // Pure pruning logic exposed as internal for unit tests.
        // Per-job cap (newest N kept per JobObjectId), then total cap (newest N overall),
        // returning the kept lines in their original file order (oldest first).
        internal static List<string> PruneLines(IReadOnlyList<string> lines, int maxPerJob, int maxTotal, out int droppedCount)
        {
            var parsed = new List<(int idx, string line, Guid jobId, DateTime started)>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (string.IsNullOrWhiteSpace(l)) continue;
                ExecutionRecord r;
                try { r = Deserialize(l); }
                catch { continue; }
                parsed.Add((i, l, r.JobObjectId, r.StartedUtc));
            }

            // Per-job cap: within each job, keep the newest maxPerJob (by StartedUtc).
            var perJobKept = parsed
                .GroupBy(p => p.jobId)
                .SelectMany(g => g.OrderByDescending(p => p.started).Take(maxPerJob))
                .ToList();

            // Global cap: keep newest maxTotal across all jobs.
            var globalKept = perJobKept
                .OrderByDescending(p => p.started)
                .Take(maxTotal)
                .ToList();

            // Restore original chronological file order so future appends remain monotonic.
            var keptIndices = new HashSet<int>(globalKept.Select(p => p.idx));
            droppedCount = parsed.Count - keptIndices.Count;

            var result = new List<string>(keptIndices.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                if (keptIndices.Contains(i)) result.Add(lines[i]);
            }
            return result;
        }

        public List<ExecutionRecord> LoadRecent()
        {
            var result = new List<ExecutionRecord>();
            if (string.IsNullOrWhiteSpace(FullPath) || !File.Exists(FullPath)) return result;

            try
            {
                string[] lines;
                lock (_writeLock)
                {
                    lines = File.ReadAllLines(FullPath);
                }

                int start = Math.Max(0, lines.Length - MaxRecordsLoaded);
                for (int i = start; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    try { result.Add(Deserialize(lines[i])); }
                    catch (Exception ex) { _log.Error($"Skip malformed record: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Read failed at '{FullPath}': {ex.Message}");
            }

            return result;
        }

        // ─── Minimal JSON (no external deps) ───────────────

        internal static string Serialize(ExecutionRecord r)
        {
            var sb = new StringBuilder("{");
            Field(sb, "RunId", r.RunId.ToString(), first: true);
            Field(sb, "JobObjectId", r.JobObjectId.ToString());
            Field(sb, "JobName", r.JobName ?? "");
            Field(sb, "StartedUtc", r.StartedUtc.ToString("o"));
            Field(sb, "FinishedUtc", r.FinishedUtc.ToString("o"));
            Field(sb, "RangeStartUtc", r.RangeStartUtc.ToString("o"));
            Field(sb, "RangeEndUtc", r.RangeEndUtc.ToString("o"));
            Field(sb, "Format", r.Format ?? "");
            Field(sb, "Trigger", r.Trigger ?? "");
            FieldRaw(sb, "Success", r.Success ? "true" : "false");
            Field(sb, "Error", r.Error ?? "");
            FieldRaw(sb, "CameraCount", r.CameraCount.ToString());
            FieldRaw(sb, "BytesWritten", r.BytesWritten.ToString());
            Field(sb, "OutputFolder", r.OutputFolder ?? "");

            sb.Append(",\"CameraNames\":[");
            if (r.CameraNames != null)
            {
                for (int i = 0; i < r.CameraNames.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(EscapeString(r.CameraNames[i] ?? ""));
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        internal static ExecutionRecord Deserialize(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart()[0] != '{')
                throw new FormatException("ExecutionRecord line must be a JSON object");

            var dict = new Dictionary<string, string>();
            var cams = new List<string>();
            ParseJson(line, dict, cams);

            return new ExecutionRecord
            {
                RunId         = TryGuid(dict, "RunId"),
                JobObjectId   = TryGuid(dict, "JobObjectId"),
                JobName       = TryStr(dict, "JobName"),
                StartedUtc    = TryDate(dict, "StartedUtc"),
                FinishedUtc   = TryDate(dict, "FinishedUtc"),
                RangeStartUtc = TryDate(dict, "RangeStartUtc"),
                RangeEndUtc   = TryDate(dict, "RangeEndUtc"),
                Format        = TryStr(dict, "Format"),
                Trigger       = TryStr(dict, "Trigger"),
                Success       = TryStr(dict, "Success") == "true",
                Error         = TryStr(dict, "Error"),
                CameraCount   = TryInt(dict, "CameraCount"),
                BytesWritten  = TryLong(dict, "BytesWritten"),
                OutputFolder  = TryStr(dict, "OutputFolder"),
                CameraNames   = cams
            };
        }

        private static void Field(StringBuilder sb, string key, string value, bool first = false)
        {
            if (!first) sb.Append(",");
            sb.Append("\"").Append(key).Append("\":").Append(EscapeString(value));
        }

        private static void FieldRaw(StringBuilder sb, string key, string raw)
        {
            sb.Append(",\"").Append(key).Append("\":").Append(raw);
        }

        private static string EscapeString(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        // Hand-rolled JSON parser tailored to ExecutionRecord shape (flat object + "CameraNames" string array).
        private static void ParseJson(string s, Dictionary<string, string> dict, List<string> camNames)
        {
            int i = 0;
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '{') return;
            i++;
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') return;
                var key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                SkipWs(s, ref i);

                if (string.Equals(key, "CameraNames", StringComparison.Ordinal))
                {
                    if (i < s.Length && s[i] == '[')
                    {
                        i++;
                        SkipWs(s, ref i);
                        while (i < s.Length && s[i] != ']')
                        {
                            SkipWs(s, ref i);
                            camNames.Add(ReadString(s, ref i));
                            SkipWs(s, ref i);
                            if (i < s.Length && s[i] == ',') i++;
                        }
                        if (i < s.Length && s[i] == ']') i++;
                    }
                }
                else if (i < s.Length && s[i] == '"')
                {
                    dict[key] = ReadString(s, ref i);
                }
                else
                {
                    var raw = ReadRaw(s, ref i);
                    dict[key] = raw;
                }

                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') i++;
            }
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return "";
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    var c = s[i + 1];
                    switch (c)
                    {
                        case '"':  sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (i + 5 < s.Length && int.TryParse(s.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out var u))
                            {
                                sb.Append((char)u);
                                i += 4;
                            }
                            break;
                        default: sb.Append(c); break;
                    }
                    i += 2;
                }
                else
                {
                    sb.Append(s[i]); i++;
                }
            }
            if (i < s.Length && s[i] == '"') i++;
            return sb.ToString();
        }

        private static string ReadRaw(string s, ref int i)
        {
            var start = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;
            return s.Substring(start, i - start).Trim();
        }

        private static string TryStr(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : "";
        private static Guid TryGuid(Dictionary<string, string> d, string k) => Guid.TryParse(TryStr(d, k), out var g) ? g : Guid.Empty;
        private static int TryInt(Dictionary<string, string> d, string k) => int.TryParse(TryStr(d, k), out var v) ? v : 0;
        private static long TryLong(Dictionary<string, string> d, string k) => long.TryParse(TryStr(d, k), out var v) ? v : 0;
        private static DateTime TryDate(Dictionary<string, string> d, string k)
            => DateTime.TryParse(TryStr(d, k), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.MinValue;
    }
}
