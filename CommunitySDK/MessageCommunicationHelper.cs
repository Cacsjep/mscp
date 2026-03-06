using System;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace CommunitySDK
{
    /// <summary>
    /// Helper for initializing and managing MessageCommunication instances.
    /// Wraps the common Start/Get/RegisterFilter/Cleanup pattern used across plugins.
    /// </summary>
    public class MessageCommunicationHelper
    {
        private MessageCommunication _mc;
        private object _filter;
        private volatile bool _registered;
        private readonly PluginLog _log;

        public MessageCommunication MessageCommunication => _mc;
        public bool IsRegistered => _registered;

        public MessageCommunicationHelper(PluginLog log)
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
                _log.Info("MessageCommunication started");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to init MessageCommunication: {ex.Message}");
                return false;
            }
        }

        public bool RegisterFilter(MessageReceiver handler, CommunicationIdFilter filter)
        {
            if (_registered || _mc == null) return false;

            try
            {
                _filter = _mc.RegisterCommunicationFilter(handler, filter);
                _registered = true;
                _log.Info("MC filter registered");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to register MC filter: {ex.Message}");
                return false;
            }
        }

        public void TransmitMessage(Message message)
        {
            _mc?.TransmitMessage(message, null, null, null);
        }

        public void Close()
        {
            if (_mc != null && _filter != null)
            {
                _mc.UnRegisterCommunicationFilter(_filter);
                _filter = null;
            }
            _mc = null;
            _registered = false;
        }
    }
}
