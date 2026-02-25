using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace Weather.Background
{
    public class WeatherBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => WeatherDefinition.WeatherBackgroundPluginId;

        public override string Name => "Weather BackgroundPlugin";

        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        public override void Init()
        {
            EnvironmentManager.Instance.Log(false, nameof(WeatherBackgroundPlugin), "Weather plugin started.");
        }

        public override void Close()
        {
            EnvironmentManager.Instance.Log(false, nameof(WeatherBackgroundPlugin), "Weather plugin stopped.");
        }
    }
}
