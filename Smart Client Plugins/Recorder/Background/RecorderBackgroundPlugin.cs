using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace Recorder.Background
{
    public class RecorderBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => RecorderDefinition.RecorderBackgroundPluginId;

        public override string Name => "Recorder BackgroundPlugin";

        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        public override void Init()
        {
            EnvironmentManager.Instance.Log(false, nameof(RecorderBackgroundPlugin), "Recorder plugin started.");
        }

        public override void Close()
        {
            EnvironmentManager.Instance.Log(false, nameof(RecorderBackgroundPlugin), "Recorder plugin stopped.");
        }
    }
}
