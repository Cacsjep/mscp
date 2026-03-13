using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace FlexView.Client
{
    public class FlexViewViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => FlexViewDefinition.ViewItemPluginId;
        public override string Name => "FlexView";
        public override bool HideSetupItem => true;

        public override VideoOSIconSourceBase IconSource
        {
            get => FlexViewDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            FlexViewDefinition.Log.Info("GenerateViewItemManager called");
            return new FlexViewViewItemManager();
        }

        public override void Init() { }
        public override void Close() { }
    }
}
