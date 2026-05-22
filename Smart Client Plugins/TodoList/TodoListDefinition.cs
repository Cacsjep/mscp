using System;
using System.Collections.Generic;
using CommunitySDK;
using FontAwesome5;
using TodoList.Client;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace TodoList
{
    public class TodoListDefinition : PluginDefinition
    {
        private static readonly VideoOSIconSourceBase _pluginIcon;

        internal static Guid TodoListPluginId = new Guid("44F910CA-22D9-470A-8303-97187DEF6B82");
        internal static Guid TodoListViewItemKind = new Guid("94D22661-BD23-448B-A7BA-0AE52EC1E9A7");

        static TodoListDefinition()
        {
            _pluginIcon = PluginIcon.RenderIconSource(EFontAwesomeIcon.Solid_ClipboardCheck);
        }

        internal static VideoOSIconSourceBase PluginIconSource => _pluginIcon;

        public override Guid Id => TodoListPluginId;

        public override string Name => "Todo List Plugin";

        public override string Manufacturer => "MSC Community Plugins";

        public override System.Drawing.Image Icon
            => VideoOS.Platform.UI.Util.ImageList.Images[VideoOS.Platform.UI.Util.PluginIx];

        public override List<ViewItemPlugin> ViewItemPlugins
            => new List<ViewItemPlugin> { new TodoListViewItemPlugin() };
    }
}
