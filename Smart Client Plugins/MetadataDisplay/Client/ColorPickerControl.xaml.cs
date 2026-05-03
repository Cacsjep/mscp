using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace MetadataDisplay.Client
{
    public partial class ColorPickerControl : UserControl
    {
        public event EventHandler ColorChanged;

        private bool _suppress;

        public ColorPickerControl()
        {
            InitializeComponent();
        }

        public string HexValue
        {
            get => hexBox.Text ?? "";
            set
            {
                if (string.Equals(hexBox.Text, value, StringComparison.OrdinalIgnoreCase)) return;
                _suppress = true;
                try
                {
                    hexBox.Text = value ?? "";
                    UpdateSwatch();
                }
                finally { _suppress = false; }
            }
        }

        private void UpdateSwatch()
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(NormalizeForWpf(hexBox.Text));
                swatch.Background = new SolidColorBrush(c);
            }
            catch
            {
                swatch.Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            }
        }

        private void OnHexChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSwatch();
            if (!_suppress) ColorChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSwatchClick(object sender, MouseButtonEventArgs e) => OpenDialog();
        private void OnPickClick(object sender, RoutedEventArgs e) => OpenDialog();

        private void OpenDialog()
        {
            using (var dlg = new WinForms.ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(NormalizeForWpf(hexBox.Text));
                    dlg.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
                }
                catch { }

                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    var picked = dlg.Color;
                    HexValue = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
                    ColorChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // Accept "#RGB", "#RRGGBB", "RRGGBB", "#AARRGGBB" etc.
        private static string NormalizeForWpf(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "#777777";
            var t = raw.Trim();
            if (!t.StartsWith("#")) t = "#" + t;
            return t;
        }
    }
}
