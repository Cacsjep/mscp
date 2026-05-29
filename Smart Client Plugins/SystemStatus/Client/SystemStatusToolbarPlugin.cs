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

            // Standard text button labeled "System Status"; the live counts go in the hover tooltip
            // (Tooltip exists from Smart Client 2022 R3, set reflectively since we compile on 22.3),
            // and the full lists are in the flyout on click.
            Title = "System Status";
            TrySet("Tooltip", SystemStatusBackgroundPlugin.Instance?.CurrentSnapshot?.ToolbarText ?? "System Status");

            if (SystemStatusBackgroundPlugin.Instance != null)
            {
                SystemStatusBackgroundPlugin.Instance.StatusChanged += OnStatusChanged;
                _subscribed = true;
            }
        }

        private bool TrySet(string propertyName, object value)
        {
            try
            {
                var p = GetType().GetProperty(propertyName);
                if (p != null && p.CanWrite && value != null) { p.SetValue(this, value); return true; }
            }
            catch { }
            return false;
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
            // Keep the "Health" label; refresh the hover tooltip with the live counts.
            try { TrySet("Tooltip", e.Snapshot.ToolbarText); }
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
