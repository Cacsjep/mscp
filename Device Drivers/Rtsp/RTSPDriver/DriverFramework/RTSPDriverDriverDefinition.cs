using System;
using System.Collections.Generic;
using System.Security;
using VideoOS.Platform.DriverFramework;
using VideoOS.Platform.DriverFramework.Data.Settings;
using VideoOS.Platform.DriverFramework.Definitions;

namespace RTSPDriver
{
    /// <summary>
    /// The main entry point for the device driver.
    /// </summary>
    public class RTSPDriverDriverDefinition : DriverDefinition
    {
        protected override Container CreateContainer(Uri uri, string userName, SecureString password, ICollection<HardwareSetting> hardwareSettings)
        {
            return new RTSPDriverContainer(this, uri, userName, password);
        }

        protected override DriverInfo CreateDriverInfo()
        {
            return new DriverInfo(Constants.DriverId, "RTSPDriver", "RTSPDriver group", Constants.DriverVersion, new[] { Constants.Product1 });
        }
    }
}
