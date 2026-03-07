using CommunitySDK;
using FontAwesome5;
using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace SmartBar.Client
{
    class SmartBarToolbarPluginInstance : WorkSpaceToolbarPluginInstance
    {
        public override void Init(Item window)
        {
            Title = "Go Back";
            Tooltip = "Go back to previous view or camera (SmartBar)";
            IconSource = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_ArrowLeft, 12);

            // Only show on main window
            if (window != null && window.FQID.ObjectId != Kind.Window)
                Visible = false;
        }

        public override void Activate()
        {
            SmartBarHistory.GoBack();
        }

        public override void Close()
        {
        }
    }

    class SmartBarToolbarPlugin : WorkSpaceToolbarPlugin
    {
        public override Guid Id => SmartBarDefinition.SmartBarToolbarId;

        public override string Name => "Smart Bar";

        public override ToolbarPluginType ToolbarPluginType => ToolbarPluginType.Action;

        public override void Init()
        {
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
            return new SmartBarToolbarPluginInstance();
        }
    }
}
