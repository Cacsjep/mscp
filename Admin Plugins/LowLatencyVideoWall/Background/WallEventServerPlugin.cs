using System;
using System.Collections.Generic;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Messaging;

namespace LowLatencyVideoWall.Background
{
    /// <summary>
    /// The server side half of the plugin. Runs in the Event Server, and also in the
    /// Management Client, because the rule engine asks the Administration environment for the
    /// action definitions while the Event Server is the environment that actually fires them.
    ///
    /// Its only real job is to own one MessageCommunication connection on behalf of the rule
    /// action. Opening a connection per rule firing would be far too expensive for an action
    /// that can be triggered many times a second.
    /// </summary>
    internal sealed class WallEventServerPlugin : BackgroundPlugin
    {
        private static readonly object Gate = new object();
        private static CrossMessageHandler _cmh;

        /// <summary>
        /// The rule action, as a singleton.
        ///
        /// BackgroundPlugin has no hook for registering an ActionManager - the rule engine only
        /// reads <c>PluginDefinition.ActionManager</c> - so the definition has to hand this
        /// instance back from that override. It lives here because this plugin owns the
        /// messaging connection the action transmits over.
        /// </summary>
        internal static WallActionManager Actions { get; } = new WallActionManager();

        public override Guid Id => LowLatencyVideoWallDefinition.BackgroundPluginId;

        public override string Name => "Low Latency Video Wall Event Server";

        public override List<EnvironmentType> TargetEnvironments => new List<EnvironmentType>
        {
            EnvironmentType.Administration,
            EnvironmentType.Service
        };

        public override void Init()
        {
            try
            {
                LowLatencyVideoWallDefinition.Log.Info(
                    $"Event Server plugin initializing in {EnvironmentManager.Instance.EnvironmentType}");

                // A failure here is not fatal. EnsureHandler retries on the first rule firing,
                // which matters because the Event Server loads plugins before the message
                // channel to the management server is necessarily up.
                EnsureHandler();

                LowLatencyVideoWallDefinition.Log.Info("Event Server plugin initialized");
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Event Server plugin Init failed: {ex.Message}", ex);
            }
        }

        public override void Close()
        {
            try
            {
                lock (Gate)
                {
                    _cmh?.Close();
                    _cmh = null;
                }

                LowLatencyVideoWallDefinition.Log.Info("Event Server plugin closed");
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Event Server plugin Close failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Sends a message to every environment connected to the site, Smart Clients included.
        /// Passing no destination is what makes it a broadcast, which is the only option here
        /// because the Event Server cannot address an individual operator's client.
        /// </summary>
        /// <returns>True if the message was handed to MessageCommunication.</returns>
        internal static bool Broadcast(string messageId, string payloadXml)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return false;

            try
            {
                var handler = EnsureHandler();
                if (handler == null)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"Cannot broadcast {messageId}: MessageCommunication is not available");
                    return false;
                }

                handler.TransmitMessage(new Message(messageId, payloadXml ?? ""));
                return true;
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Broadcasting {messageId} failed: {ex.Message}", ex);
                return false;
            }
        }

        private static CrossMessageHandler EnsureHandler()
        {
            lock (Gate)
            {
                if (_cmh != null) return _cmh;

                var handler = new CrossMessageHandler(LowLatencyVideoWallDefinition.Log);

                // A half started handler is deliberately not cached, so a rule firing later,
                // once the site is reachable, gets a fresh attempt instead of a dead object.
                if (!handler.Start() || handler.MessageCommunication == null)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        "MessageCommunication is not ready yet, will retry on the next wall switch");
                    return null;
                }

                _cmh = handler;
                LowLatencyVideoWallDefinition.Log.Info("MessageCommunication started for wall switching");
                return _cmh;
            }
        }
    }
}
