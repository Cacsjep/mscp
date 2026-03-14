using System;
using System.Linq;
using VideoOS.Platform.DriverFramework.Data;
using VideoOS.Platform.DriverFramework.Exceptions;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver
{
    /// <summary>
    /// Class for overall stream handling.
    /// </summary>
    public class RTSPDriverStreamManager : SessionEnabledStreamManager
    {
        private new RTSPDriverContainer Container => base.Container as RTSPDriverContainer;

        public RTSPDriverStreamManager(RTSPDriverContainer container) : base(container)
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
                Toolbox.Log.Trace("RTSPDriver.StreamManager.GetLiveFrame: Exception={0}", ex.Message);
                return GetLiveFrameResult.ErrorResult(StreamLiveStatus.NoConnection);
            }
        }

        internal BaseStreamSession GetSession(int channel)
        {
            return GetAllSessions().OfType<BaseRTSPDriverStreamSession>()
                .FirstOrDefault(s => s.Channel == channel);
        }

        protected override BaseStreamSession CreateSession(string deviceId, Guid streamId, Guid sessionId)
        {
            Guid dev = new Guid(deviceId);
            if (Constants.IsVideoDevice(dev))
            {
                Toolbox.Log.Trace("StreamManager: CreateSession video deviceId={0} streamId={1} sessionId={2}", deviceId, streamId, sessionId);
                return new RTSPDriverVideoStreamSession(Container.SettingsManager, Container.ConnectionManager, Container.EventManager, sessionId, deviceId, streamId);
            }

            if (Constants.IsMicrophoneDevice(dev))
            {
                Toolbox.Log.Trace("StreamManager: CreateSession audio deviceId={0} streamId={1} sessionId={2}", deviceId, streamId, sessionId);
                return new RTSPDriverAudioStreamSession(Container.SettingsManager, Container.ConnectionManager, Container.EventManager, sessionId, deviceId, streamId);
            }

            Toolbox.Log.LogError("StreamManager", "Unsupported device ID: {0}", deviceId);
            throw new MIPDriverException();
        }
    }
}
