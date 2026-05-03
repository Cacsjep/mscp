using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace MetadataDisplay.Client
{
    public class MetadataDisplayViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("C516FB0C-C0E4-467C-AE0A-7E6FAE6E7A22");

        public override string Name => "Metadata Display";

        public override VideoOSIconSourceBase IconSource
        {
            get => MetadataDisplayDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            return new MetadataDisplayViewItemManager();
        }

        public override void Init()
        {
        }

        public override void Close()
        {
        }
    }
}
