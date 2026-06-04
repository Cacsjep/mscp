using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace SystemStatus.Client
{
    /// <summary>Minimal CSV writer with a save dialog. Quotes fields as needed; UTF-8 BOM for Excel.</summary>
    internal static class Csv
    {
        public static bool Save(Window owner, string defaultName, IReadOnlyList<string> headers,
                                IEnumerable<IReadOnlyList<string>> rows)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = defaultName,
                Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                AddExtension = true
            };
            if (dlg.ShowDialog(owner) != true) return false;

            var sb = new StringBuilder();
            sb.Append(string.Join(",", headers.Select(Escape))).Append("\r\n");
            foreach (var r in rows)
                sb.Append(string.Join(",", r.Select(Escape))).Append("\r\n");

            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            return true;
        }

        private static string Escape(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
