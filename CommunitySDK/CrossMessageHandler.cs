using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace CommunitySDK
{
    /// <summary>
    /// Helper for initializing MessageCommunication and managing multiple communication filters.
    /// Wraps the common Start/Get/RegisterFilter/Cleanup pattern used across plugins.
    /// </summary>
    public class CrossMessageHandler
    {
        private MessageCommunication _mc;
        private readonly List<object> _filters = new List<object>();
        private readonly PluginLog _log;

        public MessageCommunication MessageCommunication => _mc;

        public CrossMessageHandler(PluginLog log)
        {
            _log = log;
        }

        public bool Start()
        {
            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                MessageCommunicationManager.Start(serverId);
                _mc = MessageCommunicationManager.Get(serverId);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to init MessageCommunication: {ex.Message}");
                return false;
            }
        }

        public void Register(MessageReceiver handler, CommunicationIdFilter filter)
        {
            if (_mc == null) return;
            _filters.Add(_mc.RegisterCommunicationFilter(handler, filter));
        }

        public void TransmitMessage(Message message)
        {
            _mc?.TransmitMessage(message, null, null, null);
        }

        public void Close()
        {
            if (_mc != null)
            {
                foreach (var f in _filters)
                    _mc.UnRegisterCommunicationFilter(f);
                _filters.Clear();
            }
            _mc = null;
        }
    }
}
