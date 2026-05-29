using System;

namespace AutoExporter.Background
{
    /// <summary>
    /// Parses one stderr line from AutoExporterHelper.exe. The contract:
    ///   PROGRESS cameraIdx=&lt;int&gt; total=&lt;int&gt; pct=&lt;int&gt; name=&lt;string-may-have-spaces&gt;
    /// "total" is the resolved camera count (optional for back-compat; 0 if absent).
    /// All other lines are returned as <see cref="HelperLine.Kind.Info"/> for logging only.
    /// Made internal-static so the test project can lock the format down.
    /// </summary>
    internal static class HelperProgressParser
    {
        public struct HelperLine
        {
            public enum LineKind { Progress, Info }
            public LineKind Kind;
            public int CameraIndex;
            public int Total;
            public int Percent;
            public string CameraName;
            public string Raw;
        }

        public static HelperLine Parse(string line)
        {
            var result = new HelperLine { Raw = line ?? "" };
            if (string.IsNullOrEmpty(line) || !line.StartsWith("PROGRESS ", StringComparison.Ordinal))
            {
                result.Kind = HelperLine.LineKind.Info;
                return result;
            }

            // Strip "PROGRESS "
            var rest = line.Substring("PROGRESS ".Length);

            int camIdx = 0, total = 0, pct = 0;
            string name = "";

            int cursor = 0;
            while (cursor < rest.Length)
            {
                int eq = rest.IndexOf('=', cursor);
                if (eq < 0) break;
                string key = rest.Substring(cursor, eq - cursor);
                int valStart = eq + 1;

                // The "name" value is the rest of the line (may contain spaces).
                if (key == "name")
                {
                    name = rest.Substring(valStart);
                    break;
                }

                int sp = rest.IndexOf(' ', valStart);
                int valEnd = sp < 0 ? rest.Length : sp;
                string val = rest.Substring(valStart, valEnd - valStart);

                if (key == "cameraIdx") int.TryParse(val, out camIdx);
                else if (key == "total") int.TryParse(val, out total);
                else if (key == "pct") int.TryParse(val, out pct);

                cursor = sp < 0 ? rest.Length : sp + 1;
            }

            result.Kind        = HelperLine.LineKind.Progress;
            result.CameraIndex = camIdx;
            result.Total       = total;
            result.Percent     = pct;
            result.CameraName  = name;
            return result;
        }
    }
}
