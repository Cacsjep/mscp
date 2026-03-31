using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace Timelapse.Client
{
    public class TimelapseViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => TimelapseDefinition.ViewItemPluginId;
        public override string Name => "Timelapse";
        public override bool HideSetupItem => true;

        public override VideoOSIconSourceBase IconSource
        {
            get => TimelapseDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
            => new TimelapseViewItemManager();

        public override void Init() { }
        public override void Close() { }
    }
}
