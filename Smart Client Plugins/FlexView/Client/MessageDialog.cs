using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;

namespace FlexView.Client
{
    // Dark themed popup matching the style used by the rest of the FlexView UI.
    // Uses a custom ControlTemplate so buttons don't fall back to the system theme's
    // blue hover. ShowSuccess / ShowError emit a single-button notification, Confirm
    // is a two-button OK/Cancel dialog.
    internal static class MessageDialog
    {
        private static readonly Color BackgroundColor = Color.FromRgb(0x0D, 0x11, 0x17);
        private static readonly Color BorderColor = Color.FromRgb(0x55, 0x5A, 0x5F);
        private static readonly Color TitleColor = Color.FromRgb(0xE6, 0xED, 0xF3);
        private static readonly Color BodyColor = Color.FromRgb(0xC9, 0xD1, 0xD9);

        // Accent (success / continue): GitHub-green palette.
        private static readonly Color AccentFill = Color.FromRgb(0x23, 0x86, 0x36);
        private static readonly Color AccentBorder = Color.FromRgb(0x2E, 0xA0, 0x43);
        private static readonly Color AccentHover = Color.FromRgb(0x2E, 0xA0, 0x43);
        private static readonly Color AccentPressed = Color.FromRgb(0x3A, 0xB5, 0x50);

        // Error: red.
        private static readonly Color ErrorFill = Color.FromRgb(0xC9, 0x3C, 0x37);
        private static readonly Color ErrorBorder = Color.FromRgb(0xE5, 0x53, 0x4C);
        private static readonly Color ErrorHover = Color.FromRgb(0xE5, 0x53, 0x4C);
        private static readonly Color ErrorPressed = Color.FromRgb(0xF0, 0x6A, 0x64);

        // Neutral (cancel).
        private static readonly Color NeutralFill = Color.FromRgb(0x3A, 0x3F, 0x44);
        private static readonly Color NeutralBorder = Color.FromRgb(0x55, 0x55, 0x55);
        private static readonly Color NeutralHover = Color.FromRgb(0x4A, 0x4F, 0x54);
        private static readonly Color NeutralPressed = Color.FromRgb(0x55, 0x5A, 0x5F);

        public static void ShowSuccess(string title, string body, Window owner = null)
        {
            ShowDialog(title, body, "OK", null, ButtonKind.Accent, owner);
        }

        public static void ShowError(string title, string body, Window owner = null)
        {
            ShowDialog(title, body, "OK", null, ButtonKind.Error, owner);
        }

        public static bool Confirm(string title, string body, string okText = "Continue", string cancelText = "Cancel", Window owner = null)
        {
            return ShowDialog(title, body, okText, cancelText, ButtonKind.Accent, owner) == true;
        }

        private enum ButtonKind { Accent, Error }

        private static bool? ShowDialog(string title, string body, string okText, string cancelText, ButtonKind okKind, Window owner)
        {
            bool? result = null;
            Action build = () => result = BuildAndShow(title, body, okText, cancelText, okKind, owner);
            if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                Application.Current.Dispatcher.Invoke(build);
            else
                build();
            return result;
        }

        private static bool? BuildAndShow(string title, string body, string okText, string cancelText, ButtonKind okKind, Window owner)
        {
            var dialog = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.Height,
                Width = 460,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                Background = System.Windows.Media.Brushes.Transparent,
                Owner = owner,
            };

            var outerBorder = new Border
            {
                Background = new SolidColorBrush(BackgroundColor),
                BorderBrush = new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(1),
            };

            var panel = new StackPanel { Margin = new Thickness(24) };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(TitleColor),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10),
            });

            panel.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = new SolidColorBrush(BodyColor),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18),
            });

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            if (cancelText != null)
            {
                var cancel = BuildButton(cancelText, NeutralFill, NeutralBorder, NeutralHover, NeutralPressed, false);
                cancel.Margin = new Thickness(0, 0, 8, 0);
                cancel.Click += (s, e) => { dialog.DialogResult = false; };
                buttonRow.Children.Add(cancel);
            }

            Color okFill, okBorder, okHover, okPressed;
            if (okKind == ButtonKind.Error) { okFill = ErrorFill; okBorder = ErrorBorder; okHover = ErrorHover; okPressed = ErrorPressed; }
            else { okFill = AccentFill; okBorder = AccentBorder; okHover = AccentHover; okPressed = AccentPressed; }

            var ok = BuildButton(okText ?? "OK", okFill, okBorder, okHover, okPressed, true);
            ok.IsDefault = true;
            ok.Click += (s, e) => { dialog.DialogResult = true; };
            buttonRow.Children.Add(ok);

            panel.Children.Add(buttonRow);
            outerBorder.Child = panel;
            dialog.Content = outerBorder;

            return dialog.ShowDialog();
        }

        // Builds a Button that ignores the system theme and shows our own hover/pressed colors.
        private static Button BuildButton(string content, Color fill, Color border, Color hover, Color pressed, bool semiBold)
        {
            return new Button
            {
                Content = content,
                Padding = new Thickness(20, 0, 20, 0),
                MinHeight = 32,
                MinWidth = 90,
                FontSize = 13,
                FontWeight = semiBold ? FontWeights.SemiBold : FontWeights.Normal,
                Background = new SolidColorBrush(fill),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(border),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Template = BuildButtonTemplate(hover, pressed),
            };
        }

        private static ControlTemplate BuildButtonTemplate(Color hoverFill, Color pressedFill)
        {
            string xaml =
                "<ControlTemplate TargetType='Button'" +
                "                 xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
                "                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "  <Border x:Name='border' Background='{TemplateBinding Background}'" +
                "          BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'" +
                "          Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>" +
                "    <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'" +
                "                      VerticalAlignment='{TemplateBinding VerticalContentAlignment}'" +
                "                      RecognizesAccessKey='True'/>" +
                "  </Border>" +
                "  <ControlTemplate.Triggers>" +
                "    <Trigger Property='IsMouseOver' Value='True'>" +
                $"      <Setter TargetName='border' Property='Background' Value='#{hoverFill.R:X2}{hoverFill.G:X2}{hoverFill.B:X2}'/>" +
                "    </Trigger>" +
                "    <Trigger Property='IsPressed' Value='True'>" +
                $"      <Setter TargetName='border' Property='Background' Value='#{pressedFill.R:X2}{pressedFill.G:X2}{pressedFill.B:X2}'/>" +
                "    </Trigger>" +
                "  </ControlTemplate.Triggers>" +
                "</ControlTemplate>";

            using (var sr = new StringReader(xaml))
            using (var xr = XmlReader.Create(sr))
            {
                return (ControlTemplate)XamlReader.Load(xr);
            }
        }
    }
}
