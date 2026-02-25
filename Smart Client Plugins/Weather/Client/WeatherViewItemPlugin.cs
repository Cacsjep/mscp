using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace Weather.Client
{
    public class WeatherViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("E1F2A3B4-C5D6-7890-ABCD-EF1234500004");

        public override string Name => "Weather";

        public override VideoOSIconSourceBase IconSource
        {
            get => WeatherDefinition.PluginIcon;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            return new WeatherViewItemManager();
        }

        public override void Init()
        {
        }

        public override void Close()
        {
        }
    }
}
