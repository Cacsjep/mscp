using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace CommunitySDK
{
    /// <summary>
    /// Helper for registering and bulk-unregistering Milestone message receivers.
    /// Maintains a list of registered receiver handles for centralized cleanup.
    /// </summary>
    public class EnvMessageHandler
    {
        private readonly List<object> _receivers = new List<object>();

        public void Register(MessageReceiver messageReceiver, MessageFilter messageFilter)
        {
            _receivers.Add(EnvironmentManager.Instance.RegisterReceiver(messageReceiver, messageFilter));
        }

        public void UnregisterAll()
        {
            foreach (object r in _receivers)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(r);
            }
            _receivers.Clear();
        }
    }
}
