using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace CommunitySDK
{
    /// <summary>
    /// Helper for initializing MessageCommunication and managing multiple communication filters.
    /// Wraps the common Start/Get/RegisterFilter/Cleanup pattern used across plugins.
    ///
    /// It does one thing that is not obvious and is the reason this class exists at all:
    /// it keeps pointing at the CURRENT channel. MessageCommunicationManager.Start replaces the
    /// MessageCommunication object for a site, and a management tree refresh, an Event Server
    /// restart or another plugin starting the channel is enough to trigger it. Anything holding
    /// the old reference keeps its filters on a dead object: messages simply stop arriving, with
    /// no error, no disconnect and nothing in the log, until the process is restarted. So every
    /// registration is remembered here and re-applied whenever the channel underneath changes,
    /// and the connection state event re-arms delivery after a reconnect.
    /// </summary>
    public class CrossMessageHandler
    {
        private sealed class Registration
        {
            internal MessageReceiver Handler;
            internal CommunicationIdFilter Filter;

            /// <summary>What RegisterCommunicationFilter returned, on the channel it was made on.</summary>
            internal object Token;
        }

        private readonly object _gate = new object();
        private readonly List<Registration> _registrations = new List<Registration>();
        private readonly PluginLog _log;

        private MessageCommunication _mc;
        private bool _closed;

        public CrossMessageHandler(PluginLog log)
        {
            _log = log;
        }

        /// <summary>
        /// The live channel, re-resolved on the way out so a caller never gets a stale one.
        /// Null until the channel exists.
        /// </summary>
        public MessageCommunication MessageCommunication
        {
            get
            {
                lock (_gate)
                {
                    Attach();
                    return _mc;
                }
            }
        }

        /// <summary>
        /// True when there is a channel and it is connected. TransmitMessage throws internally
        /// on a disconnected channel, so senders should check this rather than provoke it.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_gate)
                {
                    Attach();
                    return _mc != null && _mc.IsConnected;
                }
            }
        }

        public bool Start()
        {
            lock (_gate)
            {
                try
                {
                    _closed = false;

                    var serverId = EnvironmentManager.Instance.MasterSite.ServerId;

                    // Started only when there is nothing to attach to. Calling Start on a site
                    // that already has a channel is what replaces it and orphans everyone
                    // else's filters, so it is not something to do on every call.
                    if (MessageCommunicationManager.Get(serverId) == null)
                        MessageCommunicationManager.Start(serverId);

                    return Attach();
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to init MessageCommunication: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Registers a receiver and remembers it, so it survives the channel being replaced.
        /// Safe to call before the channel exists; it is applied as soon as there is one.
        /// </summary>
        public void Register(MessageReceiver handler, CommunicationIdFilter filter)
        {
            if (handler == null || filter == null) return;

            lock (_gate)
            {
                var registration = new Registration { Handler = handler, Filter = filter };
                _registrations.Add(registration);

                Attach();
                Apply(registration);
            }
        }

        /// <summary>
        /// Re-resolves the channel and re-applies any registration that is not on it yet.
        /// Called automatically, and worth calling from a host that knows it has just been
        /// through a configuration refresh. Returns true when a channel is available.
        /// </summary>
        public bool EnsureAttached()
        {
            lock (_gate) return Attach();
        }

        /// <summary>
        /// Sends on the live channel. Returns false, rather than throwing or pretending, when
        /// there is nothing connected to send on.
        /// </summary>
        public bool TransmitMessage(Message message)
        {
            if (message == null) return false;

            lock (_gate)
            {
                Attach();

                if (_closed || _mc == null || !_mc.IsConnected)
                {
                    _log.Info($"Skipping transmit of '{message.MessageId}': message communication not connected");
                    return false;
                }

                try
                {
                    _mc.TransmitMessage(message, null, null, null);
                    return true;
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to transmit '{message.MessageId}': {ex.Message}");
                    return false;
                }
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
                Detach();
                _registrations.Clear();
            }
        }

        // ---- channel plumbing ---------------------------------------------------------------

        /// <summary>
        /// Points at whatever MessageCommunication the site currently has, moving the
        /// registrations across if it is a different object from last time. Caller holds the lock.
        /// </summary>
        private bool Attach()
        {
            if (_closed) return false;

            MessageCommunication current;
            try
            {
                current = MessageCommunicationManager.Get(EnvironmentManager.Instance.MasterSite.ServerId);
            }
            catch (Exception ex)
            {
                _log.Error($"Could not read the message communication channel: {ex.Message}");
                return false;
            }

            if (current == null) return false;

            if (!ReferenceEquals(current, _mc))
            {
                Detach();

                _mc = current;
                try { _mc.ConnectionStateChangedEvent += OnConnectionStateChanged; } catch { }

                _log.Info($"Attached to the message communication channel (connected={_mc.IsConnected})");

                foreach (var registration in _registrations) Apply(registration);
                return true;
            }

            // Same channel. Only the registrations that are not on it yet, because registering
            // an id twice makes the broker log "Same messageId being registered multiple times".
            foreach (var registration in _registrations)
                if (registration.Token == null) Apply(registration);

            return true;
        }

        private void Apply(Registration registration)
        {
            if (_mc == null || registration.Token != null) return;

            try
            {
                registration.Token = _mc.RegisterCommunicationFilter(registration.Handler, registration.Filter);
            }
            catch (Exception ex)
            {
                _log.Error($"Could not register a communication filter: {ex.Message}");
            }
        }

        private void Detach()
        {
            if (_mc == null)
            {
                foreach (var registration in _registrations) registration.Token = null;
                return;
            }

            try { _mc.ConnectionStateChangedEvent -= OnConnectionStateChanged; } catch { }

            foreach (var registration in _registrations)
            {
                if (registration.Token == null) continue;
                try { _mc.UnRegisterCommunicationFilter(registration.Token); } catch { }
                registration.Token = null;
            }

            _mc = null;
        }

        /// <summary>
        /// A reconnect can hand back a different channel object, so this is where delivery is
        /// restored without the host having to notice anything happened.
        /// </summary>
        private void OnConnectionStateChanged(object sender, EventArgs e)
        {
            lock (_gate)
            {
                if (_closed) return;
                _log.Info($"Message communication connection state changed, connected={_mc?.IsConnected}");
                Attach();
            }
        }
    }
}
