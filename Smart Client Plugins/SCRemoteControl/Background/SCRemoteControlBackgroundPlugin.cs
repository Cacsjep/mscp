using System;
using System.Collections.Generic;
using SCRemoteControl.Server;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace SCRemoteControl.Background
{
    public class SCRemoteControlBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => SCRemoteControlDefinition.SCRemoteControlBackgroundPluginId;
        public override string Name => "SC Remote Control Background";

        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        public override void Init()
        {
            try
            {
                SCRemoteControlConfig.Load();
                RemoteControlServer.Instance.Start();
                SCRemoteControlDefinition.Log.Info("Background plugin initialized, server started");
            }
            catch (Exception ex)
            {
                SCRemoteControlDefinition.Log.Error("Failed to start server", ex);
            }
        }

        public override void Close()
        {
            try
            {
                RemoteControlServer.Instance.Stop();
                SCRemoteControlDefinition.Log.Info("Background plugin closed, server stopped");
            }
            catch (Exception ex)
            {
                SCRemoteControlDefinition.Log.Error("Failed to stop server", ex);
            }
        }
    }
}
