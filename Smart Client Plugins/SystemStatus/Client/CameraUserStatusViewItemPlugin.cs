using System;
using System.Drawing;
using CommunitySDK;
using FontAwesome5;
using VideoOS.Platform.Client;

namespace SystemStatus.Client
{
    /// <summary>
    /// View item shown in the Smart Client "System Overview" picker. Displays two lists - camera
    /// folders with their online/total device counts, and roles with their logged-in/total user
    /// counts - sourced from the SystemStatus background plugin. Ships inside the SystemStatus plugin.
    /// </summary>
    public class CameraUserStatusViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("F4B1C2D3-7E58-4A96-9D21-3C7A6E5B1A40");

        public override string Name => "Folder & Role - Camera User Status";

        // SDK 22.3's ViewItemPlugin takes a System.Drawing.Image icon (no IconSource), so render a
        // FontAwesome glyph to a bitmap once (lazily, on the UI thread the picker calls us on).
        private static Image _icon;
        public override Image Icon
        {
            get
            {
                if (_icon == null)
                {
                    try { _icon = PluginIcon.Render(EFontAwesomeIcon.Solid_Sitemap); }
                    catch { _icon = VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx]; }
                }
                return _icon;
            }
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            return new CameraUserStatusViewItemManager();
        }

        public override void Init() { }

        public override void Close() { }
    }
}
