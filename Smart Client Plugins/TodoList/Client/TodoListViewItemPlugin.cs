using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace TodoList.Client
{
    public class TodoListViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("50D8F5B9-F35A-4D88-A876-5689D6F6F6F7");

        public override string Name => "Todo List";

        public override VideoOSIconSourceBase IconSource
        {
            get => TodoListDefinition.PluginIconSource;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            return new TodoListViewItemManager();
        }

        public override void Init()
        {
        }

        public override void Close()
        {
        }
    }
}
