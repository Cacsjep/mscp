using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MetadataDisplay.Client.Export
{
    internal sealed class CsvOptions
    {
        public string Delimiter = ",";          // "," | ";" | "\t"
        public bool IncludeHeader = true;
        public string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
        // When true the timestamp is converted to local time before formatting.
        // The export dialog always sets this so the CSV matches the wall-clock
        // values shown in the preview.
        public bool TimestampInLocalTime = false;
        // Decimal separator ONLY affects values that parse cleanly as numbers.
        // Non-numeric values (text, lamp values like "On") are emitted verbatim.
        public string DecimalSeparator = ".";
        // Header text for the value column (single-series widgets). For Lamp
        // widgets the label column is always named "label". For multi-series
        // exports the writer takes columns explicitly.
        public string ValueHeader = "value";
        public bool IncludeLabelColumn = false;  // Lamp only
    }

    // Plain RFC 4180 CSV. The class is deliberately small - it knows about
    // delimiters, quoting and number formatting, nothing else. Time-range
    // filtering, daily windows and weekday filters all live in ArchiveExporter.
    internal static class CsvWriter
    {
        public static void WriteSingleSeries(TextWriter sink, IReadOnlyList<ExportRow> rows, CsvOptions opt)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (opt == null) opt = new CsvOptions();
            string delim = ResolveDelimiter(opt.Delimiter);
            char delimChar = delim.Length == 1 ? delim[0] : ',';

            if (opt.IncludeHeader)
            {
                sink.Write(QuoteIfNeeded("timestamp", delimChar));
                sink.Write(delim);
                sink.Write(QuoteIfNeeded(opt.ValueHeader ?? "value", delimChar));
                if (opt.IncludeLabelColumn)
                {
                    sink.Write(delim);
                    sink.Write(QuoteIfNeeded("label", delimChar));
                }
                sink.Write("\r\n");
            }

            if (rows == null) return;
            foreach (var row in rows)
            {
                sink.Write(FormatTimestamp(row.TimestampUtc, opt.TimestampFormat, opt.TimestampInLocalTime));
                sink.Write(delim);
                sink.Write(QuoteIfNeeded(FormatValue(row.Value, opt.DecimalSeparator), delimChar));
                if (opt.IncludeLabelColumn)
                {
                    sink.Write(delim);
                    sink.Write(QuoteIfNeeded(row.Label ?? string.Empty, delimChar));
                }
                sink.Write("\r\n");
            }
        }

        // Writes a wide-format multi-series CSV. seriesNames must match the
        // order of the value columns in each row's seriesValues array; missing
        // values are written as empty cells so a 3-series row missing column 1
        // becomes "ts,a,,c". Caller is responsible for merging samples by
        // timestamp before handing them in.
        public static void WriteMultiSeries(
            TextWriter sink,
            IReadOnlyList<string> seriesNames,
            IReadOnlyList<MultiSeriesExportRow> rows,
            CsvOptions opt)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (seriesNames == null) throw new ArgumentNullException(nameof(seriesNames));
            if (opt == null) opt = new CsvOptions();
            string delim = ResolveDelimiter(opt.Delimiter);
            char delimChar = delim.Length == 1 ? delim[0] : ',';

            if (opt.IncludeHeader)
            {
                sink.Write(QuoteIfNeeded("timestamp", delimChar));
                foreach (var n in seriesNames)
                {
                    sink.Write(delim);
                    sink.Write(QuoteIfNeeded(string.IsNullOrEmpty(n) ? "value" : n, delimChar));
                }
                sink.Write("\r\n");
            }

            if (rows == null) return;
            foreach (var r in rows)
            {
                sink.Write(FormatTimestamp(r.TimestampUtc, opt.TimestampFormat, opt.TimestampInLocalTime));
                for (int i = 0; i < seriesNames.Count; i++)
                {
                    sink.Write(delim);
                    string v = (r.SeriesValues != null && i < r.SeriesValues.Length) ? r.SeriesValues[i] : null;
                    if (!string.IsNullOrEmpty(v))
                        sink.Write(QuoteIfNeeded(FormatValue(v, opt.DecimalSeparator), delimChar));
                }
                sink.Write("\r\n");
            }
        }

        private static string ResolveDelimiter(string s)
        {
            if (string.IsNullOrEmpty(s)) return ",";
            // Allow the dialog to send the literal "\t" two-char string and
            // resolve it here so the combo can show a friendly "Tab" label.
            if (s == "\\t" || s == "\t") return "\t";
            return s;
        }

        private static string FormatTimestamp(DateTime utc, string fmt, bool inLocalTime)
        {
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
            DateTime t = inLocalTime ? utc.ToLocalTime() : utc;
            string defaultFmt = inLocalTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-ddTHH:mm:ssZ";
            try { return t.ToString(fmt ?? defaultFmt, CultureInfo.InvariantCulture); }
            catch { return t.ToString(defaultFmt, CultureInfo.InvariantCulture); }
        }

        // For values that parse as a double we re-emit them via the requested
        // decimal separator so Excel-DE users get "12,5" instead of "12.5".
        // Non-numeric values pass through unchanged.
        private static string FormatValue(string raw, string decimalSeparator)
        {
            if (raw == null) return string.Empty;
            if (string.IsNullOrEmpty(decimalSeparator) || decimalSeparator == ".") return raw;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return raw;
            // Use InvariantCulture string and substitute the decimal mark so we
            // never accidentally introduce thousand separators or scientific notation.
            string text = d.ToString("R", CultureInfo.InvariantCulture);
            return text.Replace(".", decimalSeparator);
        }

        // RFC 4180: quote when the value contains the delimiter, a quote, CR or LF.
        // Quotes inside a quoted value are doubled.
        private static string QuoteIfNeeded(string s, char delimiter)
        {
            if (s == null) return string.Empty;
            bool needsQuotes = false;
            foreach (var c in s)
            {
                if (c == delimiter || c == '"' || c == '\r' || c == '\n') { needsQuotes = true; break; }
            }
            if (!needsQuotes) return s;
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                if (c == '"') sb.Append('"');
                sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    internal sealed class MultiSeriesExportRow
    {
        public DateTime TimestampUtc;
        // null entries render as empty cells. Aligns with the seriesNames
        // array passed alongside.
        public string[] SeriesValues;
    }
}
