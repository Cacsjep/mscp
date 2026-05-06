using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunitySDK;
using MetadataDisplay.Client.Renderers;
using VideoOS.Platform;

namespace MetadataDisplay.Client.Export
{
    // Filter applied AFTER the archive scan returns chronological samples.
    // Captures the export-dialog "from/to + daily window + weekday picker"
    // semantics so the dialog and CSV writer share one source of truth.
    internal sealed class ExportFilter
    {
        public DateTime FromUtc;
        public DateTime ToUtc;
        // null = no time-of-day restriction. Otherwise inclusive [start, end]
        // in LOCAL time. End < Start means an overnight window (e.g. 22:00–06:00).
        public TimeSpan? DailyStartLocal;
        public TimeSpan? DailyEndLocal;
        // Bit-packed weekday mask using DayOfWeek values (0=Sun … 6=Sat).
        // 0x7F (all days) is the default; null is treated the same.
        public byte? WeekdayMask;

        public bool IsAllDays => !WeekdayMask.HasValue || (WeekdayMask.Value & 0x7F) == 0x7F;
        public bool HasDailyWindow => DailyStartLocal.HasValue && DailyEndLocal.HasValue;
    }

    // One CSV row's worth of data, source-renderer-agnostic.
    internal sealed class ExportRow
    {
        public DateTime TimestampUtc;
        public string Value;
        public string Label;     // populated for Lamp; null otherwise
    }

    // Wraps MetadataPlaybackPump.ScanRangeAsync with the export-time filtering
    // (daily window, weekday picker) and Lamp-specific label resolution. Pure
    // data layer - never touches WPF - so the dialog can preview off the UI
    // thread and unit tests don't need a Dispatcher.
    internal static class ArchiveExporter
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        // Runs a full archive scan over [from, to] and applies the supplied
        // post-scan filter. For Lamp widgets the resolved label is folded onto
        // each row using the supplied LampMap (resolved at scan time so the CSV
        // reflects what the operator currently sees, not what was mapped a year
        // ago when the data was recorded).
        public static async Task<List<ExportRow>> ScanAsync(
            Item metadataItem,
            ExtractorConfig cfg,
            ExportFilter filter,
            string lampMapForLabels,
            int maxFrames,
            CancellationToken ct)
        {
            if (metadataItem == null) return new List<ExportRow>();
            if (cfg == null || string.IsNullOrEmpty(cfg.DataKey)) return new List<ExportRow>();
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var pump = new MetadataPlaybackPump(metadataItem, _ => cfg, (_, __) => { });
            List<(string Value, DateTime TimestampUtc)> raw;
            try
            {
                raw = await pump.ScanRangeAsync(filter.FromUtc, filter.ToUtc, maxFrames, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error($"[Export] ScanRangeAsync threw: {ex.Message}", ex);
                return new List<ExportRow>();
            }

            // Resolve Lamp labels once up-front - Parse is cheap but the dict
            // lookup is hotter than re-parsing in the loop.
            Dictionary<string, string> lampLabels = null;
            if (!string.IsNullOrEmpty(lampMapForLabels))
            {
                lampLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in LampMapParser.Parse(lampMapForLabels))
                {
                    if (!string.IsNullOrEmpty(entry?.Value) && !lampLabels.ContainsKey(entry.Value))
                        lampLabels[entry.Value] = entry.Label ?? entry.Value;
                }
            }

            var rows = new List<ExportRow>(raw.Count);
            foreach (var r in raw)
            {
                ct.ThrowIfCancellationRequested();
                if (!filter.Matches(r.TimestampUtc)) continue;
                string label = null;
                if (lampLabels != null)
                {
                    // Fall back to raw value when unmapped - matches LampRenderer's
                    // unmapped-value rendering so the CSV reads the same as the UI.
                    if (!lampLabels.TryGetValue(r.Value ?? string.Empty, out label) || label == null)
                        label = r.Value ?? string.Empty;
                }
                rows.Add(new ExportRow
                {
                    TimestampUtc = r.TimestampUtc,
                    Value = r.Value ?? string.Empty,
                    Label = label,
                });
            }

            return rows;
        }

        // Multi-series scan. Walks the archive once via ScanRangeManyAsync and
        // returns one filtered ExportRow list per input config. Order matches
        // the input cfgs list - null entries / configs without a DataKey yield
        // empty result lists. Used by the export dialog when the active widget
        // is a multi-line chart so the wide-format CSV reflects every line.
        public static async Task<List<List<ExportRow>>> ScanManyAsync(
            Item metadataItem,
            IReadOnlyList<ExtractorConfig> cfgs,
            ExportFilter filter,
            int maxFrames,
            CancellationToken ct)
        {
            int n = cfgs?.Count ?? 0;
            var results = new List<List<ExportRow>>(n);
            for (int i = 0; i < n; i++) results.Add(new List<ExportRow>());
            if (metadataItem == null || n == 0) return results;
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var pump = new MetadataPlaybackPump(metadataItem, _ => null, (_, __) => { });
            List<List<(string Value, DateTime TimestampUtc)>> raw;
            try
            {
                raw = await pump.ScanRangeManyAsync(filter.FromUtc, filter.ToUtc, cfgs, maxFrames, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Error($"[Export] ScanRangeManyAsync threw: {ex.Message}", ex);
                return results;
            }

            for (int i = 0; i < n && i < raw.Count; i++)
            {
                var src = raw[i];
                if (src == null) continue;
                var dst = results[i];
                foreach (var r in src)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!filter.Matches(r.TimestampUtc)) continue;
                    dst.Add(new ExportRow
                    {
                        TimestampUtc = r.TimestampUtc,
                        Value = r.Value ?? string.Empty,
                        Label = null,
                    });
                }
            }
            return results;
        }

        // Merge parallel per-series row lists into wide-format rows keyed by
        // timestamp. Coincident timestamps across series collapse into one
        // wide row; otherwise each (series, timestamp) becomes its own row
        // with the other columns left empty. Output is ordered ascending by
        // timestamp - the natural reading order for time-series CSVs.
        public static List<MultiSeriesExportRow> MergeWide(IReadOnlyList<List<ExportRow>> perSeries)
        {
            var result = new List<MultiSeriesExportRow>();
            int n = perSeries?.Count ?? 0;
            if (n == 0) return result;

            // Bucket by timestamp ticks. Dictionary stays cheap up to a few
            // hundred thousand rows; for bigger exports the IO is the cost.
            var byTs = new Dictionary<long, MultiSeriesExportRow>();
            for (int i = 0; i < n; i++)
            {
                var list = perSeries[i];
                if (list == null) continue;
                foreach (var r in list)
                {
                    long key = r.TimestampUtc.Ticks;
                    if (!byTs.TryGetValue(key, out var wide))
                    {
                        wide = new MultiSeriesExportRow
                        {
                            TimestampUtc = r.TimestampUtc,
                            SeriesValues = new string[n],
                        };
                        byTs[key] = wide;
                    }
                    wide.SeriesValues[i] = r.Value;
                }
            }

            result.AddRange(byTs.Values);
            result.Sort((a, b) => a.TimestampUtc.Ticks.CompareTo(b.TimestampUtc.Ticks));
            return result;
        }
    }

    internal static class ExportFilterMatchExtensions
    {
        public static bool Matches(this ExportFilter f, DateTime utc)
        {
            if (utc < f.FromUtc || utc > f.ToUtc) return false;
            var local = utc.ToLocalTime();
            if (!f.IsAllDays)
            {
                int dow = (int)local.DayOfWeek; // 0=Sun … 6=Sat
                if (((f.WeekdayMask.Value >> dow) & 1) == 0) return false;
            }
            if (f.HasDailyWindow)
            {
                var tod = local.TimeOfDay;
                var s = f.DailyStartLocal.Value;
                var e = f.DailyEndLocal.Value;
                if (s == e)
                {
                    // Zero-length window matches nothing - guard against an
                    // operator typing identical start/end values.
                    return false;
                }
                if (s < e)
                {
                    if (tod < s || tod > e) return false;
                }
                else
                {
                    // Overnight wrap: e.g. 22:00 → 06:00 means 22:00–24:00 OR 00:00–06:00.
                    if (tod < s && tod > e) return false;
                }
            }
            return true;
        }
    }
}
