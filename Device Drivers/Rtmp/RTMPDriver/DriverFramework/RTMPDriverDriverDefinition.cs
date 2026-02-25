using System;
using System.Collections.Generic;
using System.Security;
using VideoOS.Platform.DriverFramework;
using VideoOS.Platform.DriverFramework.Data.Settings;
using VideoOS.Platform.DriverFramework.Definitions;

namespace RTMPDriver
{
    /// <summary>
    /// The main entry point for the device driver.
    /// </summary>
    public class RTMPDriverDriverDefinition : DriverDefinition
    {
        protected override Container CreateContainer(Uri uri, string userName, SecureString password, ICollection<HardwareSetting> hardwareSettings)
        {
            return new RTMPDriverContainer(this, uri);
        }

        protected override DriverInfo CreateDriverInfo()
        {
            return new DriverInfo(Constants.DriverId, "RTMPDriver", "RTMPDriver group", Constants.DriverVersion, new[] { Constants.Product1 });
        }
    }
}
