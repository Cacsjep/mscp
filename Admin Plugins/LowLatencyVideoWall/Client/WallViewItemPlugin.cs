using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace LowLatencyVideoWall.Client
{
    public class WallViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => LowLatencyVideoWallDefinition.ViewItemPluginId;
        public override string Name => "Low Latency Video Wall";
        public override bool HideSetupItem => true;

        public override VideoOSIconSourceBase IconSource
        {
            get => LowLatencyVideoWallDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager() => new WallViewItemManager();

        public override void Init() { }
        public override void Close() { }
    }
}
