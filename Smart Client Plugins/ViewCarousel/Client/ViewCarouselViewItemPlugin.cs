using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace ViewCarousel.Client
{
    public class ViewCarouselViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("C4446629-E01B-402F-8C8E-2C3235BCD0E3");
        public override string Name => "View Carousel";

        public override VideoOSIconSourceBase IconSource
        {
            get => ViewCarouselDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
            => new ViewCarouselViewItemManager();

        public override void Init() { }
        public override void Close() { }
    }
}
