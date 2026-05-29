using System;
using System.Collections.Generic;
using SystemStatus.Background;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace SystemStatus.Client
{
    internal class SystemStatusToolbarPluginInstance : WorkSpaceToolbarPluginInstance
    {
        private static StatusFlyoutWindow _openFlyout;
        private bool _subscribed;

        public override void Init(Item window)
        {
            // Only show on the main window, like SmartBar / TimelineJump do.
            if (window != null && window.FQID.ObjectId != Kind.Window)
                Visible = false;

            Title = SystemStatusBackgroundPlugin.Instance?.CurrentSnapshot?.ToolbarText ?? "Status";

            if (SystemStatusBackgroundPlugin.Instance != null)
            {
                SystemStatusBackgroundPlugin.Instance.StatusChanged += OnStatusChanged;
                _subscribed = true;
            }
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
                var flyout = new StatusFlyoutWindow();
                flyout.Closed += (_, __) => { if (_openFlyout == flyout) _openFlyout = null; };
                _openFlyout = flyout;
                flyout.Show();
                flyout.Activate();
            }
            catch (Exception ex)
            {
                SystemStatusDefinition.Log.Error("Failed to open status flyout", ex);
            }
        }

        public override void Close()
        {
            if (_subscribed && SystemStatusBackgroundPlugin.Instance != null)
            {
                SystemStatusBackgroundPlugin.Instance.StatusChanged -= OnStatusChanged;
                _subscribed = false;
            }
        }

        private void OnStatusChanged(object sender, StatusChangedEventArgs e)
        {
            try { Title = e.Snapshot.ToolbarText; }
            catch { /* host may not be ready yet */ }
        }
    }

    internal class SystemStatusToolbarPlugin : WorkSpaceToolbarPlugin
    {
        public override Guid Id => SystemStatusDefinition.ToolbarPluginId;

        public override string Name => "System Status";

        public override ToolbarPluginType ToolbarPluginType => ToolbarPluginType.Action;

        public override void Init()
        {
            // Show in Live and Playback workspaces.
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
            return new SystemStatusToolbarPluginInstance();
        }
    }
}
