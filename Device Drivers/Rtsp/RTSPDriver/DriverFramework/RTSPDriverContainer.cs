using System;
using System.Security;
using VideoOS.Platform.DriverFramework;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver
{
    /// <summary>
    /// Container holding all the different managers.
    /// </summary>
    public class RTSPDriverContainer : Container
    {
        public new RTSPDriverConnectionManager ConnectionManager => base.ConnectionManager as RTSPDriverConnectionManager;
        public new RTSPDriverStreamManager StreamManager => base.StreamManager as RTSPDriverStreamManager;

        /// <summary>
        /// The URI from the Add Hardware wizard (rtsp://host:port).
        /// </summary>
        public Uri HardwareUri { get; }

        /// <summary>
        /// Username from Milestone credential fields.
        /// </summary>
        public string UserName { get; }

        /// <summary>
        /// Password from Milestone credential fields.
        /// </summary>
        public SecureString Password { get; }

        public RTSPDriverContainer(DriverDefinition definition, Uri uri, string userName, SecureString password)
            : base(definition)
        {
            HardwareUri = uri;
            UserName = userName;
            Password = password;
            Toolbox.Log.Trace("RTSPDriver: Initializing container v{0}, uri={1}", Constants.DriverVersion, uri);
            base.StreamManager = new RTSPDriverStreamManager(this);
            base.ConnectionManager = new RTSPDriverConnectionManager(this);
            base.ConfigurationManager = new RTSPDriverConfigurationManager(this);
            Toolbox.Log.Trace("RTSPDriver: Container initialized, {0} channels configured", Constants.MaxDevices);
        }
    }
}
