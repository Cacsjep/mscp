using System;
using VideoOS.Platform.DriverFramework;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver
{
    /// <summary>
    /// Container holding all the different managers.
    /// </summary>
    public class RTMPDriverContainer : Container
    {
        public new RTMPDriverConnectionManager ConnectionManager => base.ConnectionManager as RTMPDriverConnectionManager;
        public new RTMPDriverStreamManager StreamManager => base.StreamManager as RTMPDriverStreamManager;

        /// <summary>
        /// The URI from the Add Hardware wizard (address + port).
        /// </summary>
        public Uri HardwareUri { get; }

        public RTMPDriverContainer(DriverDefinition definition, Uri uri)
            : base(definition)
        {
            HardwareUri = uri;
            Toolbox.Log.Trace("RTMPDriver: Initializing container v{0}, uri={1}", Constants.DriverVersion, uri);
            base.StreamManager = new RTMPDriverStreamManager(this);
            base.ConnectionManager = new RTMPDriverConnectionManager(this);
            base.ConfigurationManager = new RTMPDriverConfigurationManager(this);
            Toolbox.Log.Trace("RTMPDriver: Container initialized, {0} devices configured", Constants.MaxDevices);
        }
    }
}
