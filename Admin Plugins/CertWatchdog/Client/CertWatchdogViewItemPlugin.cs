using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace CertWatchdog.Client
{
    public class CertWatchdogViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => CertWatchdogDefinition.ViewItemPluginId;

        public override string Name => "Certificates";

        public override VideoOSIconSourceBase IconSource
        {
            get => CertWatchdogDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            return new CertWatchdogViewItemManager();
        }

        public override void Init()
        {
        }

        public override void Close()
        {
        }
    }
}
