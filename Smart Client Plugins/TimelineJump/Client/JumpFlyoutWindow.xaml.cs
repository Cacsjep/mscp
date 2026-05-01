using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VideoOS.Platform;

namespace TimelineJump.Client
{
    public partial class JumpFlyoutWindow : Window
    {
        // Quick chips - matches partner request "5, 10, 15, 30 seconds, 1 min, 5 min, 10 min, etc."
        private static readonly TimeSpan[] BackQuickJumps =
        {
            TimeSpan.FromMinutes(-10),
            TimeSpan.FromMinutes(-1),
            TimeSpan.FromSeconds(-30),
            TimeSpan.FromSeconds(-10),
        };
        private static readonly TimeSpan[] FwdQuickJumps =
        {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(10),
        };

        // Custom selectors
        private static readonly int[] CustomValues = { 1, 2, 5, 10, 15, 30, 45 };
        private sealed class UnitOption
        {
            public string Label { get; set; }
            public Func<int, TimeSpan> ToTimeSpan { get; set; }
            public override string ToString() => Label;
        }
        private static readonly UnitOption[] CustomUnits =
        {
            new UnitOption { Label = "Seconds", ToTimeSpan = v => TimeSpan.FromSeconds(v) },
            new UnitOption { Label = "Minutes", ToTimeSpan = v => TimeSpan.FromMinutes(v) },
            new UnitOption { Label = "Hours",   ToTimeSpan = v => TimeSpan.FromHours(v) },
            new UnitOption { Label = "Days",    ToTimeSpan = v => TimeSpan.FromDays(v) },
        };

        public JumpFlyoutWindow()
        {
            InitializeComponent();
            BuildQuickChips();
            BuildCustomSelectors();
            Loaded += OnLoaded;
            ContentRendered += (_, __) => PositionAtCursor();
            PreviewKeyDown += OnKeyDown;
        }

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Drag the window from anywhere except interactive controls.
            if (e.OriginalSource is System.Windows.DependencyObject src)
            {
                var hit = src as System.Windows.UIElement;
                if (hit is Button || hit is ComboBox || hit is TextBox)
                    return;
                // Walk up to skip clicks inside ComboBox dropdowns etc.
                System.Windows.DependencyObject d = src;
                while (d != null)
                {
                    if (d is Button || d is ComboBox || d is TextBox) return;
                    d = System.Windows.Media.VisualTreeHelper.GetParent(d);
                }
            }

            try { DragMove(); } catch { }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionAtCursor();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SafeClose();
                e.Handled = true;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => SafeClose();

        private bool _closing;
        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            try { Close(); } catch { }
        }

        private void PositionAtCursor()
        {
            try
            {
                var pos = System.Windows.Forms.Cursor.Position;
                var screen = System.Windows.Forms.Screen.FromPoint(pos).WorkingArea;

                // Anchor flyout below the cursor, but keep it inside the working area.
                var width = ActualWidth > 0 ? ActualWidth : 400;
                var height = ActualHeight > 0 ? ActualHeight : 320;

                double left = pos.X - width / 2;
                double top = pos.Y + 8;

                if (left < screen.Left + 8) left = screen.Left + 8;
                if (left + width > screen.Right - 8) left = screen.Right - width - 8;
                if (top + height > screen.Bottom - 8) top = pos.Y - height - 8;
                if (top < screen.Top + 8) top = screen.Top + 8;

                Left = left;
                Top = top;
            }
            catch { }
        }

        private void BuildQuickChips()
        {
            var chipStyle = (Style)FindResource("ChipButton");
            foreach (var ts in BackQuickJumps)
                quickPanelBack.Children.Add(BuildChip(ts, FormatLabel(ts, isBack: true), chipStyle));
            foreach (var ts in FwdQuickJumps)
                quickPanelFwd.Children.Add(BuildChip(ts, FormatLabel(ts, isBack: false), chipStyle));
        }

        private Button BuildChip(TimeSpan delta, string label, Style style)
        {
            var btn = new Button
            {
                Content = label,
                Style = style,
                Tag = delta,
            };
            btn.Click += OnQuickChipClick;
            return btn;
        }

        private void BuildCustomSelectors()
        {
            foreach (var v in CustomValues)
                valueCombo.Items.Add(v);
            valueCombo.SelectedIndex = 3; // 10

            foreach (var u in CustomUnits)
                unitCombo.Items.Add(u);
            unitCombo.SelectedIndex = 0; // Seconds
        }

        private static string FormatLabel(TimeSpan ts, bool isBack)
        {
            // Use absolute value for the label, and a chevron for direction.
            var abs = ts.Duration();
            string n;
            if (abs.TotalSeconds < 60) n = $"{(int)abs.TotalSeconds}s";
            else if (abs.TotalMinutes < 60) n = $"{(int)abs.TotalMinutes}m";
            else if (abs.TotalHours < 24) n = $"{(int)abs.TotalHours}h";
            else n = $"{(int)abs.TotalDays}d";
            return isBack ? $"◀ {n}" : $"{n} ▶";
        }

        private void OnQuickChipClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TimeSpan delta)
                ExecuteJump(delta);
        }

        private void OnCustomBackClick(object sender, RoutedEventArgs e)
            => ExecuteJump(GetCustomDelta(negative: true));

        private void OnCustomFwdClick(object sender, RoutedEventArgs e)
            => ExecuteJump(GetCustomDelta(negative: false));

        private TimeSpan GetCustomDelta(bool negative)
        {
            var value = valueCombo.SelectedItem is int v ? v : 10;
            var unit = unitCombo.SelectedItem as UnitOption ?? CustomUnits[0];
            var ts = unit.ToTimeSpan(value);
            return negative ? ts.Negate() : ts;
        }

        private void ExecuteJump(TimeSpan delta)
        {
            var result = PlaybackJumper.JumpBy(delta, out var detail);
            TimelineJumpDefinition.Log.Info(
                $"Jump {(delta.Ticks >= 0 ? "+" : "")}{delta} -> {result} ({detail})");
        }
    }
}
