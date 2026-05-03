using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MetadataDisplay.Client.Renderers
{
    internal sealed class LampMapEntry
    {
        public string Value;     // raw value to match (case-insensitive)
        public string Label;     // text under the lamp
        public string ColorHex;  // #RRGGBB
    }

    internal static class LampMapParser
    {
        // value=label:#RRGGBB | value=label:#RRGGBB
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
                var colon = rest.LastIndexOf(':');
                string label;
                string color;
                if (colon < 0)
                {
                    label = rest;
                    color = "#777777";
                }
                else
                {
                    label = rest.Substring(0, colon).Trim();
                    color = rest.Substring(colon + 1).Trim();
                    if (!color.StartsWith("#")) color = "#" + color;
                }
                rows.Add(new LampMapEntry { Value = value, Label = label, ColorHex = color });
            }
            return rows;
        }
    }

    internal sealed class LampRenderer
    {
        private readonly Ellipse _lamp;
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
                HorizontalAlignment = HorizontalAlignment.Center,
            };
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
            _root.Children.Add(_lamp);
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
            _lamp.Fill = new SolidColorBrush(color);
            _label.Text = hit != null ? (hit.Label ?? "") : (value ?? "—");
        }

        public void Clear()
        {
            _lamp.Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            _label.Text = "—";
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
