using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace RemoteManager.Client
{
    public class RemoteManagerViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => RemoteManagerDefinition.ViewItemPluginId;
        public override string Name => "Remote Manager";
        public override bool HideSetupItem => true;

        public override VideoOSIconSourceBase IconSource
        {
            get => RemoteManagerDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
            => new RemoteManagerViewItemManager();

        public override void Init() { }
        public override void Close() { }
    }
}
