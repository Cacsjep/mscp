using System;
using System.Windows;
using System.Windows.Threading;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;

namespace TimelineJump.Client
{
    /// <summary>
    /// Sends the master timeline to "now" when the operator enters Playback mode,
    /// gated by the JumpToCurrentOnPlayback setting. Also covers the case where
    /// the Smart Client is launched directly into the Playback workspace.
    /// </summary>
    internal static class PlaybackEntryJumper
    {
        private static readonly object _gate = new object();
        private static object _modeChangedReceiver;
        private static bool _initialized;

        public static void Init()
        {
            lock (_gate)
            {
                if (_initialized) return;
                _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                    new MessageReceiver(OnModeChanged),
                    new MessageIdFilter(MessageId.System.ModeChangedIndication));
                _initialized = true;
            }

            // Smart Client may already be in Playback when we Init (operator started
            // directly in the Playback workspace). Schedule the goto on the dispatcher
            // so the workspace finishes wiring before we send the command.
            if (EnvironmentManager.Instance.Mode == Mode.ClientPlayback)
                ScheduleJump();
        }

        public static void Close()
        {
            lock (_gate)
            {
                if (!_initialized) return;
                if (_modeChangedReceiver != null)
                {
                    try { EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver); } catch { }
                    _modeChangedReceiver = null;
                }
                _initialized = false;
            }
        }

        private static object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            if (message.Data is Mode newMode && newMode == Mode.ClientPlayback)
                ScheduleJump();
            return null;
        }

        private static void ScheduleJump()
        {
            if (!TimelineJumpConfig.JumpToCurrentOnPlayback) return;

            var app = Application.Current;
            if (app == null)
            {
                JumpNow();
                return;
            }

            try
            {
                app.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(JumpNow));
            }
            catch
            {
                JumpNow();
            }
        }

        private static void JumpNow()
        {
            try
            {
                EnvironmentManager.Instance.PostMessage(new Message(
                    MessageId.SmartClient.PlaybackCommand,
                    new PlaybackCommandData
                    {
                        Command = PlaybackData.Goto,
                        DateTime = DateTime.UtcNow
                    }));
                TimelineJumpDefinition.Log.Info("Entered Playback - jumped master timeline to current time.");
            }
            catch (Exception ex)
            {
                TimelineJumpDefinition.Log.Error("Failed to jump to current time on Playback entry", ex);
            }
        }
    }
}
