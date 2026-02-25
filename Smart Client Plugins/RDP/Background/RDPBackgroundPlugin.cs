using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace RDP.Background
{
    public class RDPBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => RDPDefinition.RDPBackgroundPluginId;

        public override string Name => "RDP BackgroundPlugin";

        /// <summary>
        /// Only run in the Smart Client environment.
        /// </summary>
        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        /// <summary>
        /// Called when the user logs in and the plugin starts.
        /// </summary>
        public override void Init()
        {
            EnvironmentManager.Instance.Log(false, nameof(RDPBackgroundPlugin), "RDP plugin started.");
        }

        /// <summary>
        /// Called when the user logs out and the plugin stops.
        /// </summary>
        public override void Close()
        {
            EnvironmentManager.Instance.Log(false, nameof(RDPBackgroundPlugin), "RDP plugin stopped.");
        }
    }
}
