using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace RDP.Client
{
    public class RDPViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("D4E5F6A7-B8C9-0123-DEFA-234567890123");

        public override string Name => "Remote Desktop Connection";

        public override VideoOSIconSourceBase IconSource
        {
            get => RDPDefinition.PluginIcon;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            return new RDPViewItemManager();
        }

        public override void Init()
        {
        }

        public override void Close()
        {
        }
    }
}
