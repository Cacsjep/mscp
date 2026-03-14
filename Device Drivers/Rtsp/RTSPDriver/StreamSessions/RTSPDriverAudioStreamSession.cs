using RTSPDriver.Rtsp;
using System;
using System.Threading;
using VideoOS.Platform.DriverFramework.Data;
using VideoOS.Platform.DriverFramework.Managers;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver
{
    /// <summary>
    /// Audio stream session that serves live audio from the primary RTSP stream.
    /// Audio is demuxed by the same RtspClientWorker that handles video for the channel.
    /// When no audio track exists, waits for the full SDK timeout then returns false
    /// so Milestone shows the error state in Management Client.
    /// </summary>
    internal class RTSPDriverAudioStreamSession : BaseRTSPDriverStreamSession
    {
        private readonly int _channelIndex;
        private readonly RtspAudioBuffer _audioBuffer;
        private bool _loggedNoAudio;

        public RTSPDriverAudioStreamSession(ISettingsManager settingsManager, RTSPDriverConnectionManager connectionManager, IEventManager eventManager, Guid sessionId, string deviceId, Guid streamId)
            : base(settingsManager, connectionManager, eventManager, sessionId, deviceId, streamId)
        {
            _channelIndex = Constants.MicrophoneChannelIndex(new Guid(deviceId));
            Channel = _channelIndex;
            _audioBuffer = connectionManager.GetOrCreateAudioBuffer(_channelIndex);
            Toolbox.Log.Trace("RTSPAudioStreamSession: Initialized channel={0}", _channelIndex + 1);
        }

        protected override bool GetLiveFrameInternal(TimeSpan timeout, out BaseDataHeader header, out byte[] data)
        {
            header = null;
            data = null;

            if (_audioBuffer == null)
            {
                Thread.Sleep((int)timeout.TotalMilliseconds);
                return false;
            }

            // Wait the full timeout for audio data to appear
            int remainingMs = (int)timeout.TotalMilliseconds;
            while (remainingMs > 0)
            {
                if (_audioBuffer.IsLive && _audioBuffer.TryGetFrame(out byte[] audioData, out DateTime timestamp))
                {
                    // Skip empty packets
                    if (audioData == null || audioData.Length == 0)
                        continue;

                    _loggedNoAudio = false;

                    int sampleRate = _audioBuffer.SampleRate;
                    int channels = _audioBuffer.Channels;
                    int bitsPerSample = _audioBuffer.BitsPerSample;
                    uint codecType = _audioBuffer.CodecType;
                    uint codecSubtype = _audioBuffer.CodecSubtype;

                    // For compressed codecs (AAC, G.711), bitsPerSample may be 0 — use raw byte length
                    int sampleCount = 0;
                    int bytesPerSample = bitsPerSample > 0 ? bitsPerSample / 8 : 0;
                    if (bytesPerSample > 0 && channels > 0)
                        sampleCount = audioData.Length / bytesPerSample / channels;

                    data = audioData;
                    header = new AudioHeader()
                    {
                        CodecType = codecType,
                        CodecSubtype = codecSubtype,
                        Timestamp = timestamp,
                        ChannelCount = channels,
                        BitsPerSample = bitsPerSample,
                        Frequency = sampleRate,
                        SampleCount = sampleCount,
                        Length = (ulong)audioData.Length,
                        SequenceNumber = _sequence++,
                    };
                    return true;
                }

                int waitMs = Math.Min(remainingMs, 500);
                _audioBuffer.WaitForFrame(waitMs);
                remainingMs -= waitMs;
            }

            // Timed out — no audio. Log once, return false so Milestone shows error state.
            if (!_loggedNoAudio)
            {
                _loggedNoAudio = true;
                Toolbox.Log.Trace("RTSPAudioStreamSession[ch{0}]: No audio data — RTSP source may not contain an audio track. " +
                    "Check your RTSP path includes audio.", _channelIndex + 1);
            }
            return false;
        }
    }
}
