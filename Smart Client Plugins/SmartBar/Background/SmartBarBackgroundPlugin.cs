using System;
using System.Collections.Generic;
using SmartBar.Client;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace SmartBar.Background
{
    /// <summary>
    /// Owns the item cache lifecycle: warms it in the background after login
    /// so the first palette open is instant, and clears it at logout.
    /// </summary>
    public class SmartBarBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => SmartBarDefinition.SmartBarBackgroundPluginId;
        public override string Name => "Smart Bar Background";

        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        public override void Init()
        {
            SmartBarItemCache.EnsureFresh();
        }

        public override void Close()
        {
            SmartBarItemCache.Invalidate();
        }
    }
}
