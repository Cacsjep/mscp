using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace Auditor.Messaging
{
    public class EnvMessageHandler
    {
        internal List<object> receivers = new List<object>();

        public void Register(MessageReceiver messageReceiver, MessageFilter messageFilter)
        {
            receivers.Add(EnvironmentManager.Instance.RegisterReceiver(messageReceiver, messageFilter));
        }

        public void UnregisterAll()
        {
            foreach (object r in receivers)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(r);
            }
            receivers.Clear();
        }
    }
}
