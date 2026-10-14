using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommunitySDK;
using LowLatencyVideoWall.Config;
using LowLatencyVideoWall.Model;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Login;
using VideoOS.Platform.Messaging;

namespace LowLatencyVideoWall.Background
{
    /// <summary>
    /// The Smart Client side listener for wall switch messages.
    ///
    /// It is a background plugin rather than part of the workspace because the Event Server
    /// broadcasts to the whole site at the moment a rule fires, which may be long before the
    /// operator opens the wall. Running outside the workspace lets the last request be held so
    /// a workspace opened afterwards still lands on the right view.
    /// </summary>
    internal sealed class WallClientBackgroundPlugin : BackgroundPlugin
    {
        // Separate from BackgroundPluginId because that id belongs to the Event Server plugin.
        // The two never load in the same environment, but sharing an id would still be wrong.
        private static readonly Guid ClientBackgroundPluginId =
            new Guid("9FC56976-1A14-47D8-98A9-4E5DEE50CF79");

        /// <summary>
        /// Raised when a rule has asked this logged in user's wall to switch. Fires on a
        /// messaging thread, so a UI subscriber must marshal to its own dispatcher.
        /// </summary>
        public static event Action<WallMessages.SetWallViewPayload> WallViewRequested;

        /// <summary>
        /// Raised when a rule composed a wall itself rather than naming a stored view.
        /// </summary>
        public static event Action<WallMessages.SetWallCamerasPayload> WallCamerasRequested;

        /// <summary>
        /// The view from the most recent request for this user, so a wall opened after the rule
        /// fired can still honour it instead of falling back to the assigned default.
        /// </summary>
        public static Guid? PendingViewProfileId { get; private set; }

        /// <summary>
        /// The last rule composed wall that arrived before a wall surface was open, so the
        /// workspace shows what the rules have already decided instead of the stored default.
        /// </summary>
        public static WallMessages.SetWallCamerasPayload PendingCameras { get; private set; }

        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(LowLatencyVideoWallDefinition.Log);

        private Timer _retryTimer;
        private volatile bool _registered;
        private volatile bool _closing;

        public override Guid Id => ClientBackgroundPluginId;

        public override string Name => "Low Latency Video Wall Client Listener";

        public override List<EnvironmentType> TargetEnvironments => new List<EnvironmentType>
        {
            EnvironmentType.SmartClient
        };

        public override void Init()
        {
            try
            {
                _cmh.Start();

                // MessageCommunication is routinely still connecting while plugins initialise,
                // so registration is retried until it takes rather than attempted once.
                if (!TryRegister())
                    _retryTimer = new Timer(OnRetry, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

                LowLatencyVideoWallDefinition.Log.Info(
                    $"Wall client listener initialized for user '{CurrentUserName()}'");
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Wall client listener Init failed: {ex.Message}", ex);
            }
        }

        public override void Close()
        {
            _closing = true;

            try
            {
                _retryTimer?.Dispose();
                _retryTimer = null;

                _cmh.Close();
                _registered = false;

                LowLatencyVideoWallDefinition.Log.Info("Wall client listener closed");
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Wall client listener Close failed: {ex.Message}", ex);
            }
        }

        private void OnRetry(object state)
        {
            if (_closing) return;

            try
            {
                if (_cmh.MessageCommunication == null) _cmh.Start();

                if (!TryRegister()) return;

                _retryTimer?.Dispose();
                _retryTimer = null;
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Wall listener retry failed: {ex.Message}", ex);
            }
        }

        private bool TryRegister()
        {
            if (_registered) return true;
            if (_cmh.MessageCommunication == null) return false;

            _cmh.Register(OnSetWallView, new CommunicationIdFilter(WallMessages.SetWallView));
            _cmh.Register(OnSetWallCameras, new CommunicationIdFilter(WallMessages.SetWallCameras));
            _registered = true;

            LowLatencyVideoWallDefinition.Log.Info(
                $"Listening for {WallMessages.SetWallView} and {WallMessages.SetWallCameras}");
            return true;
        }

        private object OnSetWallView(Message message, FQID destination, FQID source)
        {
            if (_closing) return null;

            try
            {
                var payload = WallMessages.SetWallViewPayload.FromXml(message?.Data as string);
                if (payload == null)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"{WallMessages.SetWallView} arrived without a readable payload");
                    return null;
                }

                // Every client on the site receives the broadcast, so most of them are meant to
                // ignore it. That is the normal path and is not worth a log line.
                var me = CurrentUserName();
                if (!WallConfigStore.UserMatches(payload.UserName, me)) return null;

                PendingViewProfileId = payload.ViewProfileId;
                PendingCameras = null;

                LowLatencyVideoWallDefinition.Log.Info(
                    $"Wall switch accepted for '{me}': view '{payload.ViewProfileName}' " +
                    $"({payload.ViewProfileId}), sent {payload.SentUtc:O}");

                Raise(payload);
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Handling a wall switch failed: {ex.Message}", ex);
            }

            return null;
        }

        private object OnSetWallCameras(Message message, FQID destination, FQID source)
        {
            if (_closing) return null;

            try
            {
                var payload = WallMessages.SetWallCamerasPayload.FromXml(message?.Data as string);
                if (payload == null)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"{WallMessages.SetWallCameras} arrived without a readable payload");
                    return null;
                }

                // Every client on the site receives the broadcast, so most of them are meant
                // to ignore it. That is the normal path and is not worth a log line.
                var me = CurrentUserName();
                if (!WallConfigStore.UserMatches(payload.UserName, me)) return null;

                // A stored view request is superseded by this one, or an operator who opens
                // the workspace later would land on a view the rules have moved past.
                PendingViewProfileId = null;
                PendingCameras = payload;

                LowLatencyVideoWallDefinition.Log.Info(
                    $"Wall command accepted for '{me}': {payload.Describe()}, sent {payload.SentUtc:O}");

                var handlers = WallCamerasRequested;
                if (handlers == null)
                {
                    LowLatencyVideoWallDefinition.Log.Info(
                        "No wall surface is open, the command is held as pending");
                    return null;
                }

                foreach (var handler in handlers.GetInvocationList()
                             .Cast<Action<WallMessages.SetWallCamerasPayload>>())
                {
                    try
                    {
                        handler(payload);
                    }
                    catch (Exception ex)
                    {
                        LowLatencyVideoWallDefinition.Log.Error(
                            $"A wall command subscriber threw: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Handling a wall command failed: {ex.Message}", ex);
            }

            return null;
        }

        private static void Raise(WallMessages.SetWallViewPayload payload)
        {
            var handlers = WallViewRequested;
            if (handlers == null)
            {
                LowLatencyVideoWallDefinition.Log.Info(
                    "No wall surface is open, the request is held as pending");
                return;
            }

            // Invoked one at a time so a throwing subscriber cannot stop the others, and above
            // all cannot let an exception back into the Milestone messaging thread.
            foreach (var handler in handlers.GetInvocationList().Cast<Action<WallMessages.SetWallViewPayload>>())
            {
                try
                {
                    handler(payload);
                }
                catch (Exception ex)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"A wall view subscriber threw: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// The Milestone user this Smart Client is logged in as, in whatever form the site's
        /// authentication produced. The form does not matter, since WallConfigStore.UserMatches
        /// strips any domain prefix and compares case insensitively.
        /// </summary>
        internal static string CurrentUserName()
        {
            try
            {
                // Obsolete since 2022 R1 and null under OAuth, which is precisely why it is
                // first of three and not the only one. On a Windows or basic login it is
                // still the most direct answer, so the warning is suppressed rather than the
                // source dropped.
#pragma warning disable 618
                var credential = EnvironmentManager.Instance.LoginNetworkCredential;
#pragma warning restore 618
                if (credential != null && !string.IsNullOrWhiteSpace(credential.UserName))
                {
                    return string.IsNullOrWhiteSpace(credential.Domain)
                        ? credential.UserName
                        : credential.Domain + "\\" + credential.UserName;
                }
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Reading the login credential failed: {ex.Message}");
            }

            // An external IDP login leaves no network credential behind, so the cached login
            // settings are the second source rather than a redundant one.
            try
            {
                var site = EnvironmentManager.Instance.MasterSite;
                var settings = site == null ? null : LoginSettingsCache.GetLoginSettings(site);
                if (settings == null) return "";

                if (!string.IsNullOrWhiteSpace(settings.FullyQualifiedUserName))
                    return settings.FullyQualifiedUserName;
                if (!string.IsNullOrWhiteSpace(settings.UserName))
                    return settings.UserName;

                return settings.UserIdentity ?? "";
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Reading the login settings failed: {ex.Message}");
                return "";
            }
        }
    }
}
