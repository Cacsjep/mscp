using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace SnapReport.Client
{
    public class SnapReportViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => SnapReportDefinition.ViewItemPluginId;

        public override string Name => "SnapReport";

        public override VideoOSIconSourceBase IconSource
        {
            get => SnapReportDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            return new SnapReportViewItemManager();
        }

        public override void Init()
        {
        }

        public override void Close()
        {
        }
    }
}
