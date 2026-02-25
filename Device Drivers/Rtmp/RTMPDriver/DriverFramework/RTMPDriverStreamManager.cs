using System;
using System.Linq;
using VideoOS.Platform.DriverFramework.Data;
using VideoOS.Platform.DriverFramework.Exceptions;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver
{
    /// <summary>
    /// Class for overall stream handling.
    /// </summary>
    public class RTMPDriverStreamManager : SessionEnabledStreamManager
    {
        private new RTMPDriverContainer Container => base.Container as RTMPDriverContainer;

        public RTMPDriverStreamManager(RTMPDriverContainer container) : base(container)
        {
        }

        public override GetLiveFrameResult GetLiveFrame(Guid sessionId, TimeSpan timeout)
        {
            try
            {
                return base.GetLiveFrame(sessionId, timeout);
            }
            catch (System.ServiceModel.CommunicationException ex)
            {
                Toolbox.Log.Trace("RTMPDriver.StreamManager.GetLiveFrame: Exception={0}", ex.Message);
                return GetLiveFrameResult.ErrorResult(StreamLiveStatus.NoConnection);
            }
        }

        internal BaseStreamSession GetSession(int channel)
        {
            return GetAllSessions().OfType<BaseRTMPDriverStreamSession>()
                .FirstOrDefault(s => s.Channel == channel);
        }

        protected override BaseStreamSession CreateSession(string deviceId, Guid streamId, Guid sessionId)
        {
            Guid dev = new Guid(deviceId);
            if (Constants.IsVideoDevice(dev))
            {
                Toolbox.Log.Trace("StreamManager: CreateSession deviceId={0} streamId={1} sessionId={2}", deviceId, streamId, sessionId);
                return new RTMPDriverVideoStreamSession(Container.SettingsManager, Container.ConnectionManager, Container.EventManager, sessionId, deviceId, streamId);
            }

            Toolbox.Log.LogError("StreamManager", "Unsupported device ID: {0}", deviceId);
            throw new MIPDriverException();
        }
    }
}
