using System;
using VideoOS.Platform.DriverFramework.Data;
using VideoOS.Platform.DriverFramework.Exceptions;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver
{
    /// <summary>
    /// Base stream session class. Specialized in other classes for specific devices.
    /// </summary>
    internal abstract class BaseRTSPDriverStreamSession : BaseStreamSession
    {
        public Guid Id { get; }

        public int Channel { get; protected set; }

        protected readonly string _deviceId;
        protected readonly Guid _streamId;

        protected readonly RTSPDriverConnectionManager _connectionManager;
        protected readonly ISettingsManager _settingsManager;
        protected readonly IEventManager _eventManager;

        protected int _sequence = 0;

        protected abstract bool GetLiveFrameInternal(TimeSpan timeout, out BaseDataHeader header, out byte[] data);

        public BaseRTSPDriverStreamSession(ISettingsManager settingsManager, RTSPDriverConnectionManager connectionManager, IEventManager eventManager, Guid sessionId, string deviceId, Guid streamId)
        {
            Id = sessionId;
            _settingsManager = settingsManager;
            _connectionManager = connectionManager;
            _eventManager = eventManager;
            _deviceId = deviceId;
            _streamId = streamId;
            Toolbox.Log.Trace("BaseStreamSession: Created sessionId={0} deviceId={1} streamId={2}", sessionId, deviceId, streamId);
        }

        public sealed override bool GetLiveFrame(TimeSpan timeout, out BaseDataHeader header, out byte[] data)
        {
            try
            {
                return GetLiveFrameInternal(timeout, out header, out data);
            }
            catch (Exception ex)
            {
                Toolbox.Log.LogError(GetType().Name,
                    "{0}, Channel {1}: {2}", nameof(GetLiveFrame), Channel, ex.Message + ex.StackTrace);
                throw new ConnectionLostException(ex.Message + ex.StackTrace);
            }
        }

        public override void Close()
        {
            Toolbox.Log.Trace("BaseStreamSession: Closing sessionId={0} channel={1} sequence={2}", Id, Channel, _sequence);
            _sequence = 0;
        }
    }
}
