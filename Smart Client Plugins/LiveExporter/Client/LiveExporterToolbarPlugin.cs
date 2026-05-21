using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace LiveExporter.Client
{
    internal class LiveExporterToolbarPluginInstance : WorkSpaceToolbarPluginInstance
    {
        private static LiveExporterFlyoutWindow _openFlyout;
        private object _modeChangedReceiver;

        public override void Init(Item window)
        {
            Title = "Live Exporter";

            if (window != null && window.FQID.ObjectId != Kind.Window)
                Visible = false;

            // Close the flyout if the operator leaves Live (its IP playback machinery
            // only makes sense over a live tile context).
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));

            Enabled = true;
        }

        public override void Activate()
        {
            if (_openFlyout != null)
            {
                try { _openFlyout.Close(); } catch { }
                _openFlyout = null;
                return;
            }

            try
            {
                var flyout = new LiveExporterFlyoutWindow();
                flyout.Closed += (_, __) => { if (_openFlyout == flyout) _openFlyout = null; };
                _openFlyout = flyout;
                flyout.Show();
                flyout.Activate();
            }
            catch (Exception ex)
            {
                LiveExporterDefinition.Log.Error("Failed to open Live Exporter flyout", ex);
            }
        }

        public override void Close()
        {
            if (_modeChangedReceiver != null)
            {
                try { EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver); }
                catch { }
                _modeChangedReceiver = null;
            }
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            if (message.Data is Mode newMode && newMode != Mode.ClientLive)
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
                    catch { }
                }
            }
            return null;
        }
    }

    internal class LiveExporterToolbarPlugin : WorkSpaceToolbarPlugin
    {
        public override Guid Id => LiveExporterDefinition.ToolbarPluginId;
        public override string Name => "Live Exporter";
        public override ToolbarPluginType ToolbarPluginType => ToolbarPluginType.Action;

        public override void Init()
        {
            WorkSpaceToolbarPlaceDefinition.WorkSpaceIds = new List<Guid>
            {
                ClientControl.LiveBuildInWorkSpaceId
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
            return new LiveExporterToolbarPluginInstance();
        }
    }
}
