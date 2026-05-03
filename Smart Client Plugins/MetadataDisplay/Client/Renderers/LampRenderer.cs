using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FontAwesome5;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class LampMapEntry
    {
        public string Value;     // raw value to match (case-insensitive)
        public string Label;     // text under the lamp
        public string ColorHex;  // #RRGGBB
        public string IconName;  // EFontAwesomeIcon name (e.g. "Solid_Bell"); empty = colored circle
    }

    internal static class LampMapParser
    {
        // value=label:#RRGGBB[:IconName] | ... (semicolons in value/label are not supported)
        public static List<LampMapEntry> Parse(string s)
        {
            var rows = new List<LampMapEntry>();
            if (string.IsNullOrWhiteSpace(s)) return rows;
            foreach (var raw in s.Split('|'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var value = part.Substring(0, eq).Trim();
                var rest = part.Substring(eq + 1).Trim();
                string label = rest;
                string color = "#777777";
                string icon = "";

                // rest is "label[:color[:icon]]"
                var colon1 = rest.LastIndexOf(':');
                if (colon1 >= 0)
                {
                    var beforeLast = rest.Substring(0, colon1).Trim();
                    var afterLast = rest.Substring(colon1 + 1).Trim();
                    var colon2 = beforeLast.LastIndexOf(':');
                    if (colon2 >= 0 && IsLikelyColor(beforeLast.Substring(colon2 + 1).Trim()))
                    {
                        label = beforeLast.Substring(0, colon2).Trim();
                        color = beforeLast.Substring(colon2 + 1).Trim();
                        icon = afterLast;
                    }
                    else if (IsLikelyColor(afterLast))
                    {
                        label = beforeLast;
                        color = afterLast;
                    }
                    else
                    {
                        // afterLast is icon name (no color present)
                        label = beforeLast;
                        icon = afterLast;
                    }
                    if (!color.StartsWith("#")) color = "#" + color;
                }

                rows.Add(new LampMapEntry { Value = value, Label = label, ColorHex = color, IconName = icon });
            }
            return rows;
        }

        private static bool IsLikelyColor(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var t = s.StartsWith("#") ? s.Substring(1) : s;
            if (t.Length != 3 && t.Length != 6 && t.Length != 8) return false;
            foreach (var ch in t)
                if (!IsHexChar(ch)) return false;
            return true;
        }

        private static bool IsHexChar(char c)
            => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        public static string Serialize(IEnumerable<LampMapEntry> rows)
        {
            var parts = new List<string>();
            foreach (var r in rows)
            {
                if (r == null || string.IsNullOrEmpty(r.Value)) continue;
                var color = string.IsNullOrEmpty(r.ColorHex) ? "#777777" : r.ColorHex;
                if (!color.StartsWith("#")) color = "#" + color;
                var s = $"{r.Value}={r.Label ?? ""}:{color}";
                if (!string.IsNullOrEmpty(r.IconName)) s += ":" + r.IconName;
                parts.Add(s);
            }
            return string.Join("|", parts);
        }

        public static bool TryParseIcon(string name, out EFontAwesomeIcon icon)
        {
            if (string.IsNullOrEmpty(name)) { icon = EFontAwesomeIcon.None; return false; }
            return Enum.TryParse(name, out icon) && icon != EFontAwesomeIcon.None;
        }
    }

    internal sealed class LampRenderer
    {
        private readonly Grid _glyphHost;
        private readonly Ellipse _lamp;
        private readonly ImageAwesome _icon;
        private readonly TextBlock _label;
        private readonly StackPanel _root;

        public LampRenderer()
        {
            _lamp = new Ellipse
            {
                Width = 96,
                Height = 96,
                Fill = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 2,
            };
            _icon = new ImageAwesome
            {
                Width = 88,
                Height = 88,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                Visibility = Visibility.Collapsed,
            };
            _glyphHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _glyphHost.Children.Add(_lamp);
            _glyphHost.Children.Add(_icon);
            _label = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD7, 0xDA)),
                FontSize = 16,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            _root = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _root.Children.Add(_glyphHost);
            _root.Children.Add(_label);
        }

        public UIElement Visual => _root;

        public void Update(string value, IList<LampMapEntry> map)
        {
            LampMapEntry hit = null;
            if (value != null && map != null)
            {
                foreach (var row in map)
                {
                    if (string.Equals(row.Value, value, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = row;
                        break;
                    }
                }
            }

            var color = hit != null ? ColorUtil.Parse(hit.ColorHex, Colors.Gray) : Color.FromRgb(0x77, 0x77, 0x77);
            ApplyGlyph(hit?.IconName, color);
            _label.Text = hit != null ? (hit.Label ?? "") : (value ?? "—");
        }

        public void Clear()
        {
            ApplyGlyph(null, Color.FromRgb(0x55, 0x55, 0x55));
            _label.Text = "—";
        }

        private void ApplyGlyph(string iconName, Color color)
        {
            if (LampMapParser.TryParseIcon(iconName, out var fa))
            {
                _icon.Icon = fa;
                _icon.Foreground = new SolidColorBrush(color);
                _icon.Visibility = Visibility.Visible;
                _lamp.Visibility = Visibility.Collapsed;
            }
            else
            {
                _lamp.Fill = new SolidColorBrush(color);
                _lamp.Visibility = Visibility.Visible;
                _icon.Visibility = Visibility.Collapsed;
            }
        }
    }

    internal static class ColorUtil
    {
        public static Color Parse(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            try
            {
                var obj = ColorConverter.ConvertFromString(hex);
                if (obj is Color c) return c;
            }
            catch { }
            return fallback;
        }
    }
}
