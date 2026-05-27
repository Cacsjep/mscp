using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace MetadataDisplay.Client
{
    // Shared XML syntax-coloring for read-only inspector views and for the
    // editable import dialog. Brushes mirror the dark-theme palette used by the
    // rest of the plugin (Admin/MetadataViewer style):
    //   <,>,/ and "..."           : dim gray
    //   element name              : light blue
    //   attribute name            : salmon
    //   attribute value           : light orange
    //   text content              : default foreground
    internal static class XmlHighlighter
    {
        public static readonly SolidColorBrush BrushBracket    = MakeFrozen(Color.FromRgb(0x80, 0x80, 0x80));
        public static readonly SolidColorBrush BrushElement    = MakeFrozen(Color.FromRgb(0x6B, 0xB6, 0xFF));
        public static readonly SolidColorBrush BrushAttr       = MakeFrozen(Color.FromRgb(0xE6, 0x95, 0x80));
        public static readonly SolidColorBrush BrushAttrValue  = MakeFrozen(Color.FromRgb(0xE6, 0xB8, 0x6B));
        public static readonly SolidColorBrush BrushText       = MakeFrozen(Color.FromRgb(0xE6, 0xEA, 0xEC));

        private static SolidColorBrush MakeFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static readonly Regex XmlTokenRegex = new Regex(
            @"(?<tag><(?<close>/?)(?<elname>[\w:.\-]+)(?<attrs>(\s+[\w:.\-]+\s*=\s*""[^""]*"")*)\s*(?<self>/?)>)|(?<text>[^<]+)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex AttrRegex = new Regex(
            @"(?<name>[\w:.\-]+)\s*=\s*""(?<value>[^""]*)""",
            RegexOptions.Compiled);

        // Walks the supplied XML and appends colored Run elements into `para`.
        // Robust to malformed input: anything the regex can't tokenize is emitted
        // as default-foreground text so the user still sees what they typed.
        public static void HighlightInto(Paragraph para, string xml)
        {
            if (para == null) return;
            if (string.IsNullOrEmpty(xml)) return;

            int i = 0;
            foreach (Match m in XmlTokenRegex.Matches(xml))
            {
                if (m.Index > i)
                    para.Inlines.Add(new Run(xml.Substring(i, m.Index - i)) { Foreground = BrushText });

                if (m.Groups["text"].Success)
                {
                    para.Inlines.Add(new Run(m.Value) { Foreground = BrushText });
                }
                else
                {
                    para.Inlines.Add(new Run("<" + m.Groups["close"].Value) { Foreground = BrushBracket });
                    para.Inlines.Add(new Run(m.Groups["elname"].Value) { Foreground = BrushElement });
                    var attrs = m.Groups["attrs"].Value;
                    if (!string.IsNullOrEmpty(attrs))
                    {
                        int last = 0;
                        foreach (Match am in AttrRegex.Matches(attrs))
                        {
                            if (am.Index > last)
                                para.Inlines.Add(new Run(attrs.Substring(last, am.Index - last)) { Foreground = BrushBracket });
                            para.Inlines.Add(new Run(am.Groups["name"].Value) { Foreground = BrushAttr });
                            para.Inlines.Add(new Run("=\"") { Foreground = BrushBracket });
                            para.Inlines.Add(new Run(am.Groups["value"].Value) { Foreground = BrushAttrValue });
                            para.Inlines.Add(new Run("\"") { Foreground = BrushBracket });
                            last = am.Index + am.Length;
                        }
                        if (last < attrs.Length)
                            para.Inlines.Add(new Run(attrs.Substring(last)) { Foreground = BrushBracket });
                    }
                    var selfClose = m.Groups["self"].Value;
                    para.Inlines.Add(new Run(selfClose + ">") { Foreground = BrushBracket });
                }

                i = m.Index + m.Length;
            }
            if (i < xml.Length)
                para.Inlines.Add(new Run(xml.Substring(i)) { Foreground = BrushText });
        }
    }
}
