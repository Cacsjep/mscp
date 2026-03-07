using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

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

            if (e.Key != Key.Space)
                return;

            if (Keyboard.FocusedElement is TextBoxBase ||
                Keyboard.FocusedElement is PasswordBox)
                return;

            // Don't trigger from our own window
            if (sender is SmartBarWindow)
                return;

            e.Handled = true;

            _isOpen = true;
            var window = new SmartBarWindow();
            window.Closed += (_, __) => _isOpen = false;
            window.Show();
        }
    }
}
