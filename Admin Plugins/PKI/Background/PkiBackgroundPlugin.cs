using System;
using System.Collections.Generic;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace PKI.Background
{
    // Service-environment hook on the Management Server. v1 has no server-side
    // operations because the Management Client UI does the crypto + storage
    // locally (CertVault DPAPI). Kept as a stub so the registration call from
    // PKIDefinition compiles and the plugin shows up in the Service env.
    public class PkiBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => PKIDefinition.BackgroundPluginId;
        public override string Name => "PKI Background";
        public override List<EnvironmentType> TargetEnvironments =>
            new List<EnvironmentType> { EnvironmentType.Service };

        public override void Init() { PKIDefinition.Log.Info("PkiBackgroundPlugin Init"); }
        public override void Close() { PKIDefinition.Log.Info("PkiBackgroundPlugin Close"); }
    }
}
