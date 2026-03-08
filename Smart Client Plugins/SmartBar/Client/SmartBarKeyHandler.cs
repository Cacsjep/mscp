using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartBar.Client
{
    static class SmartBarKeyHandler
    {
        private static bool _installed;
        private static bool _isOpen;

        public static void Install()
        {
            if (_installed)
                return;

            EventManager.RegisterClassHandler(
                typeof(Window),
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(OnPreviewKeyDown));

            _installed = true;
        }

        public static void Uninstall()
        {
            _installed = false;
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_installed || _isOpen)
                return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key != SmartBarConfig.InvokeKey)
                return;
            if (Keyboard.Modifiers != SmartBarConfig.InvokeModifiers)
                return;

            if (Keyboard.FocusedElement is TextBoxBase ||
                Keyboard.FocusedElement is PasswordBox)
                return;

            // Don't trigger from our own window
            if (sender is SmartBarWindow)
                return;

            // Don't trigger while settings panel is open
            if (Keyboard.FocusedElement is DependencyObject focused)
            {
                for (DependencyObject d = focused; d != null; d = VisualTreeHelper.GetParent(d))
                {
                    if (d is SmartBarSettingsPanelControl)
                        return;
                }
            }

            e.Handled = true;

            _isOpen = true;
            var window = new SmartBarWindow();
            window.Closed += (_, __) => _isOpen = false;
            window.Show();
        }
    }
}
