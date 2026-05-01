using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace TimelineJump.Client
{
    internal class TimelineJumpToolbarPluginInstance : WorkSpaceToolbarPluginInstance
    {
        private static JumpFlyoutWindow _openFlyout;

        public override void Init(Item window)
        {
            Title = "Jump";
            Tooltip = "Jump backward or forward on the playback timeline";

            // Only show on the main window, like SmartBar does.
            if (window != null && window.FQID.ObjectId != Kind.Window)
                Visible = false;
        }

        public override void Activate()
        {
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
        }
    }

    internal class TimelineJumpToolbarPlugin : WorkSpaceToolbarPlugin
    {
        public override Guid Id => TimelineJumpDefinition.ToolbarPluginId;

        public override string Name => "Timeline Jump";

        public override ToolbarPluginType ToolbarPluginType => ToolbarPluginType.Action;

        public override void Init()
        {
            // Both Live and Playback workspaces - we handle live by switching the
            // selected camera into independent playback at (now + delta).
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
