using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CommunitySDK;

namespace MetadataDisplay.Client
{
    // Y-axis assignment for a series. "Right" only renders the right-axis ticks
    // and labels when at least one series uses it; otherwise the chart looks
    // identical to the single-axis layout.
    internal enum LineSeriesAxis { Left, Right }

    // Per-series threshold band. Mirrors the shared NumericConfig fields but
    // travels with each series so dual-axis multi-line charts can have one
    // band per signal at the right Y-coordinate. All-zero / empty = disabled.
    internal sealed class LineSeriesThreshold
    {
        public bool Enabled;
        public double? Min;
        public double? Max;
        public bool HighIsBad = true;
        public string ColorOk = "#3CB371";
        public string ColorWarn = "#E69500";
        public string ColorBad = "#D8392C";
    }

    // One line on the chart. Topic / SourceFilters / DataKey form the extraction
    // tuple - packets that match all three feed this series's bucket buffer.
    // Display-only fields (Name, Color, Thickness, LineType) drive how the line
    // is painted; YAxis routes between left and right axes; Threshold attaches
    // an optional colored band painted behind this series only.
    internal sealed class LineSeries
    {
        public string Topic = string.Empty;
        public string TopicMatchMode = "Exact";  // Contains | Exact | EndsWith
        public string SourceFilters = string.Empty;  // semicolon-joined name=value pairs
        public string DataKey = string.Empty;
        public string Name = string.Empty;        // legend label; falls back to DataKey when empty
        public string Color = "#FF4FC3F7";
        public double Thickness = 2;
        public string LineType = "Straight";      // Straight | Smooth | Step
        public LineSeriesAxis YAxis = LineSeriesAxis.Left;
        public bool Visible = true;               // session-only toggle (not persisted)
        public bool FillEnabled = true;           // translucent area below the line
        public bool ShowMarker;                   // dot at every plotted point
        public LineSeriesThreshold Threshold = new LineSeriesThreshold();

        // Topic-or-DataKey is the minimum to be live. Renderer skips invalid
        // series silently rather than throwing so a half-typed accordion row in
        // the configuration window doesn't blow up the preview.
        public bool IsExtractable => !string.IsNullOrWhiteSpace(DataKey);

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? (DataKey ?? string.Empty) : Name;

        public ExtractorConfig ToExtractorConfig()
        {
            return new ExtractorConfig
            {
                Topic = Topic ?? string.Empty,
                TopicMatchMode = string.IsNullOrEmpty(TopicMatchMode) ? "Exact" : TopicMatchMode,
                SourceFilters = ExtractorConfig.ParseSourceFilters(SourceFilters ?? string.Empty),
                DataKey = DataKey ?? string.Empty,
            };
        }
    }

    // Hand-rolled JSON serializer for the LineSeries list. Avoids pulling
    // System.Text.Json (already ILRepacked into the assembly via LiveCharts2's
    // dependency tree) into the public API surface so this class works whether
    // the shipped MetadataDisplay.dll has it merged or external.
    //
    // Schema:
    //   [ { "topic":"…", "matchMode":"Exact", "sourceFilters":"…",
    //       "dataKey":"…", "name":"…", "color":"#RRGGBB",
    //       "thickness":2, "lineType":"Straight", "yAxis":"Left",
    //       "th": { "on":false, "min":null, "max":null, "highBad":true,
    //               "ok":"#…", "warn":"#…", "bad":"#…" } }, … ]
    //
    // Unknown keys are ignored on read; missing keys take their POCO defaults.
    internal static class LineSeriesParser
    {
        public const int MaxSeries = 8;
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        // Resolves the line-series list a Line Chart should render. Modern
        // widgets store the list as a JSON blob in LineSeriesJson; legacy
        // widgets (built before commit 2 of [2.9.0]) store nothing in that
        // blob and instead carry single-series settings on the manager
        // directly. This helper hands the renderer a stable, non-empty list
        // either way so callers don't have to special-case migration.
        public static List<LineSeries> LoadFromManager(MetadataDisplayViewItemManager m)
        {
            if (m == null) return new List<LineSeries>();
            var json = m.LineSeriesJson;
            if (!string.IsNullOrWhiteSpace(json))
            {
                var modern = Deserialize(json);
                if (modern.Count > 0) return modern;
            }
            return new List<LineSeries> { LegacyFromManager(m) };
        }

        // Synthesize one LineSeries entry from the manager's legacy fields.
        // Mirrors the original single-series LineChart: same Topic / DataKey
        // / colors / thickness / line type, plus the global thresholds folded
        // onto the series so an upgraded widget with thresholds enabled keeps
        // showing its existing band.
        public static LineSeries LegacyFromManager(MetadataDisplayViewItemManager m)
        {
            var s = new LineSeries
            {
                Topic = m.Topic ?? string.Empty,
                TopicMatchMode = string.IsNullOrEmpty(m.TopicMatchMode) ? "Exact" : m.TopicMatchMode,
                SourceFilters = m.SourceFilters ?? string.Empty,
                DataKey = m.DataKey ?? string.Empty,
                Name = string.Empty,
                Color = string.IsNullOrWhiteSpace(m.LineColor) ? "#FF4FC3F7" : m.LineColor,
                LineType = string.IsNullOrEmpty(m.LineType) ? "Straight" : m.LineType,
                YAxis = LineSeriesAxis.Left,
                FillEnabled = !string.Equals(m.LineFill, "false", StringComparison.OrdinalIgnoreCase),
                ShowMarker = string.Equals(m.LineShowMarker, "true", StringComparison.OrdinalIgnoreCase),
            };
            if (double.TryParse(m.LineThickness, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var t) && t > 0)
                s.Thickness = t;
            s.Threshold = new LineSeriesThreshold
            {
                Enabled = string.Equals(m.ThresholdsEnabled, "true", StringComparison.OrdinalIgnoreCase),
                Min = ParseNullableDouble(m.NumMin),
                Max = ParseNullableDouble(m.NumMax),
                HighIsBad = !string.Equals(m.NumDirection, "LowIsBad", StringComparison.OrdinalIgnoreCase),
                ColorOk = string.IsNullOrWhiteSpace(m.ColorOk) ? "#3CB371" : m.ColorOk,
                ColorWarn = string.IsNullOrWhiteSpace(m.ColorWarn) ? "#E69500" : m.ColorWarn,
                ColorBad = string.IsNullOrWhiteSpace(m.ColorBad) ? "#D8392C" : m.ColorBad,
            };
            return s;
        }

        private static double? ParseNullableDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }

        public static List<LineSeries> Deserialize(string json)
        {
            var list = new List<LineSeries>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                int pos = 0;
                SkipWs(json, ref pos);
                if (pos >= json.Length || json[pos] != '[') return list;
                pos++;
                SkipWs(json, ref pos);
                while (pos < json.Length && json[pos] != ']')
                {
                    var s = ReadSeries(json, ref pos);
                    if (s != null) list.Add(s);
                    SkipWs(json, ref pos);
                    if (pos < json.Length && json[pos] == ',') { pos++; SkipWs(json, ref pos); }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"[LineSeries] Deserialize failed: {ex.Message} - falling back to empty list", ex);
                return new List<LineSeries>();
            }
            if (list.Count > MaxSeries)
            {
                _log.Info($"[LineSeries] Trimmed {list.Count - MaxSeries} excess series (cap is {MaxSeries})");
                list.RemoveRange(MaxSeries, list.Count - MaxSeries);
            }
            return list;
        }

        public static string Serialize(IReadOnlyList<LineSeries> list)
        {
            if (list == null || list.Count == 0) return "[]";
            var sb = new StringBuilder();
            sb.Append('[');
            int n = Math.Min(list.Count, MaxSeries);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                WriteSeries(sb, list[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void WriteSeries(StringBuilder sb, LineSeries s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('{');
            WriteString(sb, "topic", s.Topic);
            sb.Append(',');
            WriteString(sb, "matchMode", s.TopicMatchMode);
            sb.Append(',');
            WriteString(sb, "sourceFilters", s.SourceFilters);
            sb.Append(',');
            WriteString(sb, "dataKey", s.DataKey);
            sb.Append(',');
            WriteString(sb, "name", s.Name);
            sb.Append(',');
            WriteString(sb, "color", s.Color);
            sb.Append(',');
            WriteNumber(sb, "thickness", s.Thickness);
            sb.Append(',');
            WriteString(sb, "lineType", s.LineType);
            sb.Append(',');
            WriteString(sb, "yAxis", s.YAxis == LineSeriesAxis.Right ? "Right" : "Left");
            sb.Append(',');
            sb.Append("\"fill\":").Append(s.FillEnabled ? "true" : "false").Append(',');
            sb.Append("\"marker\":").Append(s.ShowMarker ? "true" : "false").Append(',');
            sb.Append("\"th\":");
            WriteThreshold(sb, s.Threshold ?? new LineSeriesThreshold());
            sb.Append('}');
        }

        private static void WriteThreshold(StringBuilder sb, LineSeriesThreshold t)
        {
            sb.Append('{');
            sb.Append("\"on\":").Append(t.Enabled ? "true" : "false").Append(',');
            sb.Append("\"min\":");
            if (t.Min.HasValue) sb.Append(t.Min.Value.ToString("R", CultureInfo.InvariantCulture));
            else sb.Append("null");
            sb.Append(',');
            sb.Append("\"max\":");
            if (t.Max.HasValue) sb.Append(t.Max.Value.ToString("R", CultureInfo.InvariantCulture));
            else sb.Append("null");
            sb.Append(',');
            sb.Append("\"highBad\":").Append(t.HighIsBad ? "true" : "false").Append(',');
            WriteString(sb, "ok", t.ColorOk); sb.Append(',');
            WriteString(sb, "warn", t.ColorWarn); sb.Append(',');
            WriteString(sb, "bad", t.ColorBad);
            sb.Append('}');
        }

        private static void WriteString(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append('"').Append(':');
            sb.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '"':  sb.Append("\\\""); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        private static void WriteNumber(StringBuilder sb, string key, double value)
        {
            sb.Append('"').Append(key).Append('"').Append(':');
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        // --- minimal recursive-descent JSON reader ---

        private static LineSeries ReadSeries(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length || s[pos] != '{') { pos++; return null; }
            pos++;
            var series = new LineSeries();
            SkipWs(s, ref pos);
            while (pos < s.Length && s[pos] != '}')
            {
                string key = ReadString(s, ref pos);
                SkipWs(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') break;
                pos++;
                SkipWs(s, ref pos);
                ApplyKey(series, key, s, ref pos);
                SkipWs(s, ref pos);
                if (pos < s.Length && s[pos] == ',') { pos++; SkipWs(s, ref pos); }
            }
            if (pos < s.Length && s[pos] == '}') pos++;
            return series;
        }

        private static void ApplyKey(LineSeries series, string key, string s, ref int pos)
        {
            switch (key)
            {
                case "topic":         series.Topic = ReadString(s, ref pos); break;
                case "matchMode":     series.TopicMatchMode = ReadString(s, ref pos); break;
                case "sourceFilters": series.SourceFilters = ReadString(s, ref pos); break;
                case "dataKey":       series.DataKey = ReadString(s, ref pos); break;
                case "name":          series.Name = ReadString(s, ref pos); break;
                case "color":         series.Color = ReadString(s, ref pos); break;
                case "thickness":     series.Thickness = ReadNumber(s, ref pos) ?? series.Thickness; break;
                case "lineType":      series.LineType = ReadString(s, ref pos); break;
                case "yAxis":
                    {
                        var v = ReadString(s, ref pos);
                        series.YAxis = string.Equals(v, "Right", StringComparison.OrdinalIgnoreCase)
                            ? LineSeriesAxis.Right : LineSeriesAxis.Left;
                        break;
                    }
                case "fill":   series.FillEnabled = ReadBool(s, ref pos) ?? series.FillEnabled; break;
                case "marker": series.ShowMarker  = ReadBool(s, ref pos) ?? series.ShowMarker;  break;
                case "th":
                    series.Threshold = ReadThreshold(s, ref pos);
                    break;
                default: SkipValue(s, ref pos); break;
            }
        }

        private static LineSeriesThreshold ReadThreshold(string s, ref int pos)
        {
            var t = new LineSeriesThreshold();
            SkipWs(s, ref pos);
            if (pos >= s.Length || s[pos] != '{') { SkipValue(s, ref pos); return t; }
            pos++;
            SkipWs(s, ref pos);
            while (pos < s.Length && s[pos] != '}')
            {
                string key = ReadString(s, ref pos);
                SkipWs(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') break;
                pos++;
                SkipWs(s, ref pos);
                switch (key)
                {
                    case "on":      t.Enabled = ReadBool(s, ref pos) ?? false; break;
                    case "min":     t.Min = ReadNumber(s, ref pos); break;
                    case "max":     t.Max = ReadNumber(s, ref pos); break;
                    case "highBad": t.HighIsBad = ReadBool(s, ref pos) ?? true; break;
                    case "ok":      t.ColorOk = ReadString(s, ref pos); break;
                    case "warn":    t.ColorWarn = ReadString(s, ref pos); break;
                    case "bad":     t.ColorBad = ReadString(s, ref pos); break;
                    default:        SkipValue(s, ref pos); break;
                }
                SkipWs(s, ref pos);
                if (pos < s.Length && s[pos] == ',') { pos++; SkipWs(s, ref pos); }
            }
            if (pos < s.Length && s[pos] == '}') pos++;
            return t;
        }

        private static string ReadString(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length || s[pos] != '"') return string.Empty;
            pos++;
            var sb = new StringBuilder();
            while (pos < s.Length && s[pos] != '"')
            {
                char c = s[pos++];
                if (c == '\\' && pos < s.Length)
                {
                    char esc = s[pos++];
                    switch (esc)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"':  sb.Append('"');  break;
                        case '/':  sb.Append('/');  break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 <= s.Length && int.TryParse(s.Substring(pos, 4),
                                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            {
                                sb.Append((char)code);
                                pos += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else sb.Append(c);
            }
            if (pos < s.Length && s[pos] == '"') pos++;
            return sb.ToString();
        }

        private static double? ReadNumber(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos < s.Length && (s[pos] == 'n' || s[pos] == 'N'))
            {
                // null
                while (pos < s.Length && s[pos] != ',' && s[pos] != '}' && s[pos] != ']') pos++;
                return null;
            }
            int start = pos;
            if (pos < s.Length && (s[pos] == '-' || s[pos] == '+')) pos++;
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-' || (c >= '0' && c <= '9')) pos++;
                else break;
            }
            if (pos == start) return null;
            var token = s.Substring(start, pos - start);
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null;
        }

        private static bool? ReadBool(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos + 4 <= s.Length && string.Equals(s.Substring(pos, 4), "true", StringComparison.OrdinalIgnoreCase))
            { pos += 4; return true; }
            if (pos + 5 <= s.Length && string.Equals(s.Substring(pos, 5), "false", StringComparison.OrdinalIgnoreCase))
            { pos += 5; return false; }
            SkipValue(s, ref pos);
            return null;
        }

        private static void SkipWs(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        private static void SkipValue(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length) return;
            char c = s[pos];
            if (c == '"') { ReadString(s, ref pos); return; }
            if (c == '{' || c == '[')
            {
                char open = c; char close = c == '{' ? '}' : ']';
                int depth = 0;
                while (pos < s.Length)
                {
                    char ch = s[pos++];
                    if (ch == '"') { pos--; ReadString(s, ref pos); continue; }
                    if (ch == open) depth++;
                    else if (ch == close) { depth--; if (depth == 0) return; }
                }
                return;
            }
            // bare literal - number / true / false / null
            while (pos < s.Length && s[pos] != ',' && s[pos] != '}' && s[pos] != ']') pos++;
        }
    }
}
