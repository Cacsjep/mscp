using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace Notepad.Background
{
    public class NotepadBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => NotepadDefinition.NotepadBackgroundPluginId;

        public override string Name => "Notepad BackgroundPlugin";

        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        public override void Init()
        {
            EnvironmentManager.Instance.Log(false, nameof(NotepadBackgroundPlugin), "Notepad plugin started.");
        }

        public override void Close()
        {
            EnvironmentManager.Instance.Log(false, nameof(NotepadBackgroundPlugin), "Notepad plugin stopped.");
        }
    }
}
