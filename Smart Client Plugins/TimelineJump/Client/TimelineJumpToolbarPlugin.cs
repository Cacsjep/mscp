using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace TimelineJump.Client
{
    internal class TimelineJumpToolbarPluginInstance : WorkSpaceToolbarPluginInstance
    {
        private static JumpFlyoutWindow _openFlyout;
        private bool _subscribed;
        private object _modeChangedReceiver;

        public override void Init(Item window)
        {
            Title = "Jump";

            // Only show on the main window, like SmartBar does.
            if (window != null && window.FQID.ObjectId != Kind.Window)
                Visible = false;

            // In Playback workspace the button is always usable; in Live it should only
            // light up when at least one tile is in independent playback. Track that
            // dynamically so the operator sees it enable/disable as they toggle the
            // per-tile independent-playback button on the camera.
            ImageViewerHelper.IndependentPlaybackStateChanged += OnIndependentPlaybackStateChanged;
            _subscribed = true;

            // Close the flyout when the operator leaves Playback (Live/Setup), otherwise
            // it sits orphaned over a workspace where it cannot do anything useful.
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));

            UpdateEnabled();
        }

        public override void Activate()
        {
            // If we're in Live and nothing is in independent playback there is no
            // meaningful timeline to jump on. Bail out quietly to avoid crashing the
            // client when the operator clicks the button without a target tile.
            if (!IsActionable())
            {
                TimelineJumpDefinition.Log.Info(
                    "Jump activated in Live mode without a tile in independent playback - ignored.");
                return;
            }

            // Toggle: a second click closes an open flyout.
            if (_openFlyout != null)
            {
                try { _openFlyout.Close(); } catch { }
                _openFlyout = null;
                return;
            }

            try
            {
                var flyout = new JumpFlyoutWindow();
                flyout.Closed += (_, __) => { if (_openFlyout == flyout) _openFlyout = null; };
                _openFlyout = flyout;
                flyout.Show();
                flyout.Activate();
            }
            catch (Exception ex)
            {
                TimelineJumpDefinition.Log.Error("Failed to open jump flyout", ex);
            }
        }

        public override void Close()
        {
            if (_subscribed)
            {
                ImageViewerHelper.IndependentPlaybackStateChanged -= OnIndependentPlaybackStateChanged;
                _subscribed = false;
            }
            if (_modeChangedReceiver != null)
            {
                try { EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver); } catch { }
                _modeChangedReceiver = null;
            }
        }

        private void OnIndependentPlaybackStateChanged(object sender, EventArgs e)
        {
            // Event can fire on a non-UI thread. WorkSpaceToolbarPluginInstance.Enabled
            // is observed by the Smart Client toolbar host and is safe to set from any
            // thread, but we still want to keep the work tiny here.
            UpdateEnabled();
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            // Fired on a non-UI thread; route the close to the flyout's Dispatcher.
            if (message.Data is Mode newMode && newMode != Mode.ClientPlayback)
            {
                var flyout = _openFlyout;
                if (flyout != null)
                {
                    try
                    {
                        flyout.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { flyout.Close(); } catch { }
                        }));
                    }
                    catch { /* dispatcher may already be shut down */ }
                }
            }
            UpdateEnabled();
            return null;
        }

        private void UpdateEnabled()
        {
            try { Enabled = IsActionable(); }
            catch { /* toolbar host may not be ready yet during Init */ }
        }

        private static bool IsActionable()
        {
            if (EnvironmentManager.Instance.Mode == Mode.ClientPlayback) return true;
            // Live mode: only when some tile is currently in independent playback.
            return ImageViewerHelper.AnyIndependentPlayback();
        }
    }

    internal class TimelineJumpToolbarPlugin : WorkSpaceToolbarPlugin
    {
        public override Guid Id => TimelineJumpDefinition.ToolbarPluginId;

        public override string Name => "Timeline Jump";

        public override ToolbarPluginType ToolbarPluginType => ToolbarPluginType.Action;

        public override void Init()
        {
            // Live + Playback workspaces. In Live the instance disables itself unless
            // an independent-playback tile exists.
            WorkSpaceToolbarPlaceDefinition.WorkSpaceIds = new List<Guid>
            {
                ClientControl.LiveBuildInWorkSpaceId,
                ClientControl.PlaybackBuildInWorkSpaceId
            };
            WorkSpaceToolbarPlaceDefinition.WorkSpaceStates = new List<WorkSpaceState>
            {
                WorkSpaceState.Normal
            };
        }

        public override void Close()
        {
        }

        public override WorkSpaceToolbarPluginInstance GenerateWorkSpaceToolbarPluginInstance()
        {
            return new TimelineJumpToolbarPluginInstance();
        }
    }
}
