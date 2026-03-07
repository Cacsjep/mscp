using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace SmartBar.Client
{
    static class SmartBarHistory
    {
        private static readonly LinkedList<FQID> _history = new LinkedList<FQID>();
        private const int MaxHistory = 10;
        private static object _viewReceiver;
        private static bool _suppressNext;

        public static void Install()
        {
            _viewReceiver = EnvironmentManager.Instance.RegisterReceiver(
                OnViewChanged,
                new MessageIdFilter(MessageId.SmartClient.SelectedViewChangedIndication));
        }

        public static void Uninstall()
        {
            if (_viewReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_viewReceiver);
                _viewReceiver = null;
            }
        }

        private static object OnViewChanged(Message message, FQID dest, FQID source)
        {
            if (_suppressNext)
            {
                _suppressNext = false;
                return null;
            }

            var viewItem = message.Data as ViewAndLayoutItem;
            if (viewItem?.FQID == null) return null;

            // Don't add duplicate if same as last
            if (_history.Count > 0 && _history.Last.Value.ObjectId == viewItem.FQID.ObjectId)
                return null;

            _history.AddLast(viewItem.FQID);
            if (_history.Count > MaxHistory)
                _history.RemoveFirst();

            System.Diagnostics.Debug.WriteLine($"[SmartBar] View history push: {viewItem.Name} (total: {_history.Count})");
            return null;
        }

        public static bool CanGoBack => _history.Count > 1;

        public static void GoBack()
        {
            // Need at least 2 entries: current + previous
            if (_history.Count < 2)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartBar] GoBack: not enough history ({_history.Count})");
                return;
            }

            // Remove current
            _history.RemoveLast();

            // Navigate to previous
            var previousView = _history.Last.Value;
            _suppressNext = true;

            var windows = Configuration.Instance.GetItemsByKind(Kind.Window);
            var dest = windows.Count > 0 ? windows[0].FQID : null;

            System.Diagnostics.Debug.WriteLine($"[SmartBar] GoBack: navigating to previous view");

            EnvironmentManager.Instance.SendMessage(
                new Message(MessageId.SmartClient.MultiWindowCommand,
                    new MultiWindowCommandData
                    {
                        MultiWindowCommand = MultiWindowCommand.SetViewInWindow,
                        View = previousView,
                        Window = dest
                    }), dest);
        }
    }
}
