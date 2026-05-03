using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace MetadataDisplay.Client
{
    public partial class ColorPickerControl : UserControl
    {
        public event EventHandler ColorChanged;

        private string _hex = "#777777";

        public ColorPickerControl()
        {
            InitializeComponent();
            UpdateSwatch();
        }

        public string HexValue
        {
            get => _hex;
            set
            {
                var v = string.IsNullOrWhiteSpace(value) ? "#777777" : value.Trim();
                if (string.Equals(_hex, v, StringComparison.OrdinalIgnoreCase)) return;
                _hex = v;
                UpdateSwatch();
            }
        }

        private void UpdateSwatch()
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(NormalizeForWpf(_hex));
                swatch.Background = new SolidColorBrush(c);
            }
            catch
            {
                swatch.Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            }
        }

        private void OnSwatchClick(object sender, MouseButtonEventArgs e)
        {
            using (var dlg = new WinForms.ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(NormalizeForWpf(_hex));
                    dlg.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                }
                catch { }

                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    var picked = dlg.Color;
                    _hex = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
                    UpdateSwatch();
                    ColorChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private static string NormalizeForWpf(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "#777777";
            var t = raw.Trim();
            if (!t.StartsWith("#")) t = "#" + t;
            return t;
        }
    }
}
