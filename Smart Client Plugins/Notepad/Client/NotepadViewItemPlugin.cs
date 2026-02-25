using System;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI.Controls;

namespace Notepad.Client
{
    public class NotepadViewItemPlugin : ViewItemPlugin
    {
        public override Guid Id => new Guid("F1A2B3C4-D5E6-7890-ABCD-EF1234600004");

        public override string Name => "Notepad";

        public override VideoOSIconSourceBase IconSource
        {
            get => NotepadDefinition.PluginIcon;
            protected set => base.IconSource = value;
        }

        public override ViewItemManager GenerateViewItemManager()
        {
            return new NotepadViewItemManager();
        }

        public override void Init()
        {
        }

        public override void Close()
        {
        }
    }
}
