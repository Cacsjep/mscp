using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTSPDriver.Rtsp
{
    /// <summary>
    /// Per-channel RTSP pull client using FFmpeg.
    /// Connects to an RTSP source, demuxes video and audio, and pushes frames to buffers.
    /// Prepends SPS/PPS extradata to keyframes for Milestone's decoder.
    /// </summary>
    internal unsafe class RtspClientWorker
    {
        private readonly int _channelIndex;
        private readonly string _rtspUrl;
        private readonly string _transport; // "tcp", "udp", or "auto"
        private readonly int _connectionTimeoutSec;
        private readonly int _reconnectIntervalSec;
        private readonly int _rtpBufferSizeKB;
        private readonly RtspStreamBuffer _buffer;
        private readonly RtspAudioBuffer _audioBuffer;

        private Thread _thread;
        private volatile bool _running;
        private volatile string _lastError;
        private volatile int _reconnectAttempt;
        private volatile RtspWorkerState _state = RtspWorkerState.Idle;
        private DateTime _connectedSince = DateTime.MinValue;

        public string LastError => _lastError;
        public int ReconnectAttempt => _reconnectAttempt;
        public RtspWorkerState State => _state;
        public DateTime ConnectedSince => _connectedSince;

        /// <summary>
        /// The RTSP URL with credentials masked for display purposes.
        /// </summary>
        public string DisplayUrl
        {
            get
            {
                try
                {
                    var uri = new Uri(_rtspUrl);
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                        return _rtspUrl.Replace(uri.UserInfo + "@", "***@");
                    return _rtspUrl;
                }
                catch
                {
                    return _rtspUrl;
                }
            }
        }

        public string TransportProtocol => _transport;

        public RtspClientWorker(int channelIndex, string rtspUrl, string transport,
            int connectionTimeoutSec, int reconnectIntervalSec, int rtpBufferSizeKB,
            RtspStreamBuffer buffer, RtspAudioBuffer audioBuffer = null)
        {
            _channelIndex = channelIndex;
            _rtspUrl = rtspUrl;
            _transport = (transport ?? "auto").ToLowerInvariant();
            _connectionTimeoutSec = connectionTimeoutSec;
            _reconnectIntervalSec = reconnectIntervalSec;
            _rtpBufferSizeKB = rtpBufferSizeKB;
            _buffer = buffer;
            _audioBuffer = audioBuffer;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(WorkerLoop)
            {
                Name = $"RTSP-Channel{_channelIndex + 1}",
                IsBackground = true
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            if (_thread != null)
            {
                _thread.Join(5000);
                _thread = null;
            }
            _state = RtspWorkerState.Idle;
        }

        private void WorkerLoop()
        {
            SetupFfmpegPath();

            while (_running)
            {
                _state = RtspWorkerState.Connecting;
                _reconnectAttempt++;
                _lastError = null;

                Toolbox.Log.Trace("RtspClientWorker[{0}]: Connecting attempt={1} url={2} transport={3}",
                    _channelIndex + 1, _reconnectAttempt, DisplayUrl, _transport);

                try
                {
                    RunSession();
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    Toolbox.Log.LogError("RtspClientWorker",
                        "Channel {0}: Session error: {1}", _channelIndex + 1, ex.Message);
                }

                _buffer.SetOffline();
                _audioBuffer?.SetOffline();
                _connectedSince = DateTime.MinValue;

                if (!_running) break;

                _state = RtspWorkerState.Reconnecting;
                Toolbox.Log.Trace("RtspClientWorker[{0}]: {1} — reconnecting in {2}s (attempt {3})",
                    _channelIndex + 1, _lastError ?? "Disconnected", _reconnectIntervalSec, _reconnectAttempt);

                // Wait for reconnect interval, checking _running periodically
                for (int i = 0; i < _reconnectIntervalSec * 10 && _running; i++)
                    Thread.Sleep(100);
            }

            _state = RtspWorkerState.Idle;
        }

        private void RunSession()
        {
            AVFormatContext* fmtCtx = null;
            AVPacket* pkt = null;

            try
            {
                fmtCtx = ffmpeg.avformat_alloc_context();
                if (fmtCtx == null)
                {
                    _lastError = "Failed to allocate format context";
                    return;
                }

                // Set RTSP options
                AVDictionary* opts = null;
                if (_transport == "tcp")
                    ffmpeg.av_dict_set(&opts, "rtsp_transport", "tcp", 0);
                else if (_transport == "udp")
                    ffmpeg.av_dict_set(&opts, "rtsp_transport", "udp", 0);
                // else "auto": FFmpeg defaults to UDP, which is preferred for LAN surveillance

                long timeoutUs = _connectionTimeoutSec * 1_000_000L;
                ffmpeg.av_dict_set(&opts, "stimeout", timeoutUs.ToString(), 0);     // RTSP socket connect/setup timeout
                ffmpeg.av_dict_set(&opts, "rw_timeout", timeoutUs.ToString(), 0);    // I/O read/write timeout (detects camera unplug)
                ffmpeg.av_dict_set(&opts, "timeout", timeoutUs.ToString(), 0);       // General socket timeout
                ffmpeg.av_dict_set(&opts, "buffer_size", (_rtpBufferSizeKB * 1024).ToString(), 0);
                ffmpeg.av_dict_set(&opts, "max_delay", "500000", 0);
                ffmpeg.av_dict_set(&opts, "fflags", "nobuffer", 0);
                ffmpeg.av_dict_set(&opts, "analyzeduration", "2000000", 0);
                ffmpeg.av_dict_set(&opts, "probesize", "2000000", 0);

                int ret = ffmpeg.avformat_open_input(&fmtCtx, _rtspUrl, null, &opts);
                if (ret < 0)
                {
                    _lastError = ClassifyOpenError(ret);
                    _state = RtspWorkerState.Error;
                    return;
                }

                ret = ffmpeg.avformat_find_stream_info(fmtCtx, null);
                if (ret < 0)
                {
                    _lastError = $"Could not find stream info: {FfmpegError(ret)}";
                    _state = RtspWorkerState.Error;
                    return;
                }

                // Find video and audio streams
                int videoStreamIndex = -1;
                int audioStreamIndex = -1;
                AVCodecID codecId = AVCodecID.AV_CODEC_ID_NONE;
                AVCodecID audioCodecId = AVCodecID.AV_CODEC_ID_NONE;
                for (int i = 0; i < (int)fmtCtx->nb_streams; i++)
                {
                    var type = fmtCtx->streams[i]->codecpar->codec_type;
                    if (type == AVMediaType.AVMEDIA_TYPE_VIDEO && videoStreamIndex < 0)
                    {
                        videoStreamIndex = i;
                        codecId = fmtCtx->streams[i]->codecpar->codec_id;
                    }
                    else if (type == AVMediaType.AVMEDIA_TYPE_AUDIO && audioStreamIndex < 0)
                    {
                        audioStreamIndex = i;
                        audioCodecId = fmtCtx->streams[i]->codecpar->codec_id;
                    }
                }

                if (videoStreamIndex < 0)
                {
                    _lastError = "No video stream found in RTSP source";
                    _state = RtspWorkerState.NoVideoTrack;
                    return;
                }

                bool isHevc = codecId == AVCodecID.AV_CODEC_ID_HEVC;
                bool isH264 = codecId == AVCodecID.AV_CODEC_ID_H264;

                if (!isH264 && !isHevc)
                {
                    string codecName = ffmpeg.avcodec_get_name(codecId);
                    _lastError = $"Unsupported codec: {codecName}. Only H.264 and H.265/HEVC are supported.";
                    _state = RtspWorkerState.UnsupportedCodec;
                    return;
                }

                var codecpar = fmtCtx->streams[videoStreamIndex]->codecpar;
                string detectedCodec = isHevc ? "H.265/HEVC" : "H.264/AVC";
                int width = codecpar->width;
                int height = codecpar->height;

                // Extract extradata (SPS/PPS for H.264, VPS/SPS/PPS for HEVC).
                // RTSP delivers Annex B packets but SPS/PPS are only in extradata,
                // not in the packet stream. We must prepend them to keyframes for Milestone's decoder.
                byte[] extradata = null;
                if (codecpar->extradata != null && codecpar->extradata_size > 0)
                {
                    extradata = new byte[codecpar->extradata_size];
                    Marshal.Copy((IntPtr)codecpar->extradata, extradata, 0, codecpar->extradata_size);
                }

                _buffer.SetStreamInfo(detectedCodec, width, height);

                // Setup audio stream if present and audio buffer is provided
                AVRational audioTimeBase = default;
                byte[] aacAdtsHeader = null;
                if (audioStreamIndex >= 0 && _audioBuffer != null)
                {
                    var audioParams = fmtCtx->streams[audioStreamIndex]->codecpar;
                    int audioSampleRate = audioParams->sample_rate;
                    int audioChannels = audioParams->ch_layout.nb_channels;
                    if (audioChannels <= 0) audioChannels = 1;
                    int audioBits = audioParams->bits_per_coded_sample > 0 ? audioParams->bits_per_coded_sample : 16;
                    audioTimeBase = fmtCtx->streams[audioStreamIndex]->time_base;

                    var mapping = MapAudioCodec(audioCodecId);
                    _audioBuffer.SetStreamInfo(audioSampleRate, audioChannels, audioBits, mapping.codecType, mapping.codecSubtype);

                    // For AAC, prepare ADTS header template from extradata (AudioSpecificConfig).
                    // FFmpeg's RTSP demuxer delivers raw AAC Access Units without ADTS framing.
                    if (audioCodecId == AVCodecID.AV_CODEC_ID_AAC && audioParams->extradata != null && audioParams->extradata_size >= 2)
                    {
                        byte[] asc = new byte[audioParams->extradata_size];
                        Marshal.Copy((IntPtr)audioParams->extradata, asc, 0, asc.Length);
                        aacAdtsHeader = BuildAdtsTemplate(asc);
                        Toolbox.Log.Trace("RtspClientWorker[{0}]: AAC ADTS header template built from {1}B AudioSpecificConfig",
                            _channelIndex + 1, asc.Length);
                    }

                    _audioBuffer.SetLive();

                    Toolbox.Log.Trace("RtspClientWorker[{0}]: Audio found codec={1} rate={2} ch={3} bits={4} adts={5}",
                        _channelIndex + 1, ffmpeg.avcodec_get_name(audioCodecId), audioSampleRate, audioChannels, audioBits, aacAdtsHeader != null ? "yes" : "no");
                }
                else if (_audioBuffer != null)
                {
                    Toolbox.Log.Trace("RtspClientWorker[{0}]: No audio stream in RTSP source", _channelIndex + 1);
                }

                _state = RtspWorkerState.AwaitingKeyFrame;
                _connectedSince = DateTime.UtcNow;
                _reconnectAttempt = 0;

                Toolbox.Log.Trace("RtspClientWorker[{0}]: Connected codec={1} {2}x{3} transport={4} extradata={5}B audio={6}, awaiting keyframe",
                    _channelIndex + 1, detectedCodec, width, height, _transport, extradata?.Length ?? 0, audioStreamIndex >= 0 ? "yes" : "no");

                var timeBase = fmtCtx->streams[videoStreamIndex]->time_base;
                long firstVideoPts = ffmpeg.AV_NOPTS_VALUE;
                long firstAudioPts = ffmpeg.AV_NOPTS_VALUE;
                int totalFrames = 0;
                int keyFrames = 0;
                int audioFrames = 0;
                bool gotFirstKeyFrame = false;

                // Read loop — packets are already Annex B from RTSP demuxer
                pkt = ffmpeg.av_packet_alloc();
                while (_running)
                {
                    ret = ffmpeg.av_read_frame(fmtCtx, pkt);
                    if (ret < 0)
                    {
                        if (ret == ffmpeg.AVERROR_EOF)
                            _lastError = "Stream ended (EOF)";
                        else
                            _lastError = $"Read error: {FfmpegError(ret)}";
                        break;
                    }

                    // Handle audio packets
                    if (pkt->stream_index == audioStreamIndex && _audioBuffer != null && pkt->size > 0)
                    {
                        byte[] rawAudio = new byte[pkt->size];
                        Marshal.Copy((IntPtr)pkt->data, rawAudio, 0, pkt->size);

                        // Prepend ADTS header for AAC if needed
                        byte[] audioData;
                        if (aacAdtsHeader != null && !StartsWithAdtsSync(rawAudio))
                        {
                            int frameLen = 7 + rawAudio.Length;
                            audioData = new byte[frameLen];
                            Buffer.BlockCopy(aacAdtsHeader, 0, audioData, 0, 7);
                            // Patch frame length into ADTS header (13 bits across bytes 3-4)
                            audioData[3] = (byte)((audioData[3] & 0xFC) | ((frameLen >> 11) & 0x03));
                            audioData[4] = (byte)((frameLen >> 3) & 0xFF);
                            audioData[5] = (byte)(((frameLen & 0x07) << 5) | 0x1F);
                            audioData[6] = 0xFC; // buffer fullness = 0x7FF (VBR), 0 raw frames
                            Buffer.BlockCopy(rawAudio, 0, audioData, 7, rawAudio.Length);
                        }
                        else
                        {
                            audioData = rawAudio;
                        }

                        DateTime audioTs = DateTime.UtcNow;
                        if (pkt->pts != ffmpeg.AV_NOPTS_VALUE)
                        {
                            if (firstAudioPts == ffmpeg.AV_NOPTS_VALUE)
                                firstAudioPts = pkt->pts;
                            double relSec = (pkt->pts - firstAudioPts) * ffmpeg.av_q2d(audioTimeBase);
                            audioTs = _connectedSince.AddSeconds(relSec);
                        }

                        _audioBuffer.PushFrame(audioData, audioTs);
                        audioFrames++;
                        if (audioFrames == 1 || audioFrames % 500 == 0)
                        {
                            Toolbox.Log.Trace("RtspClientWorker[{0}]: Audio frame #{1} size={2}B",
                                _channelIndex + 1, audioFrames, pkt->size);
                        }
                        ffmpeg.av_packet_unref(pkt);
                        continue;
                    }

                    if (pkt->stream_index != videoStreamIndex)
                    {
                        ffmpeg.av_packet_unref(pkt);
                        continue;
                    }

                    bool isKeyFrame = (pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                    totalFrames++;
                    if (isKeyFrame) keyFrames++;

                    // Skip all frames until first keyframe — decoder can't start without SPS/PPS + IDR
                    if (!gotFirstKeyFrame)
                    {
                        if (!isKeyFrame)
                        {
                            ffmpeg.av_packet_unref(pkt);
                            continue;
                        }
                        gotFirstKeyFrame = true;
                        _buffer.SetLive();
                        _state = RtspWorkerState.Streaming;
                        Toolbox.Log.Trace("RtspClientWorker[{0}]: First keyframe at frame #{1}, size={2}, streaming",
                            _channelIndex + 1, totalFrames, pkt->size);
                    }

                    // Compute timestamp relative to first video PTS
                    DateTime frameTs = DateTime.UtcNow;
                    if (pkt->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        if (firstVideoPts == ffmpeg.AV_NOPTS_VALUE)
                            firstVideoPts = pkt->pts;
                        double relativeSeconds = (pkt->pts - firstVideoPts) * ffmpeg.av_q2d(timeBase);
                        frameTs = _connectedSince.AddSeconds(relativeSeconds);
                    }

                    byte[] pktData = new byte[pkt->size];
                    Marshal.Copy((IntPtr)pkt->data, pktData, 0, pkt->size);

                    // On keyframes, prepend extradata (SPS/PPS) if not already present in packet.
                    // RTSP demuxer delivers Annex B but puts SPS/PPS in extradata only.
                    byte[] frameData;
                    if (isKeyFrame && extradata != null && extradata.Length > 0 && !StartsWithParamSets(pktData, isHevc))
                    {
                        frameData = new byte[extradata.Length + pktData.Length];
                        Buffer.BlockCopy(extradata, 0, frameData, 0, extradata.Length);
                        Buffer.BlockCopy(pktData, 0, frameData, extradata.Length, pktData.Length);
                    }
                    else
                    {
                        frameData = pktData;
                    }

                    _buffer.PushFrame(frameData, isKeyFrame, isHevc, frameTs);
                    ffmpeg.av_packet_unref(pkt);
                }

                Toolbox.Log.Trace("RtspClientWorker[{0}]: Session ended totalFrames={1} keyFrames={2} audioFrames={3}",
                    _channelIndex + 1, totalFrames, keyFrames, audioFrames);
            }
            finally
            {
                if (pkt != null)
                {
                    var p = pkt;
                    ffmpeg.av_packet_free(&p);
                }

                if (fmtCtx != null)
                    ffmpeg.avformat_close_input(&fmtCtx);
            }
        }

        /// <summary>
        /// Check if packet data already starts with SPS (H.264 NAL type 7) or VPS (HEVC NAL type 32).
        /// If so, extradata is already inline and we don't need to prepend it.
        /// </summary>
        private static bool StartsWithParamSets(byte[] data, bool isHevc)
        {
            if (data == null || data.Length < 5) return false;
            // Must start with Annex B start code
            if (data[0] != 0 || data[1] != 0 || data[2] != 0 || data[3] != 1) return false;

            if (isHevc)
            {
                int nalType = (data[4] >> 1) & 0x3F;
                return nalType == 32; // VPS
            }
            else
            {
                int nalType = data[4] & 0x1F;
                return nalType == 7; // SPS
            }
        }

        /// <summary>
        /// Classify FFmpeg open errors into user-friendly messages.
        /// </summary>
        private string ClassifyOpenError(int errorCode)
        {
            string ffmpegMsg = FfmpegError(errorCode);

            // Log the raw FFmpeg error for diagnostics
            Toolbox.Log.Trace("RtspClientWorker[{0}]: FFmpeg error {1}: {2}",
                _channelIndex + 1, errorCode, ffmpegMsg);

            if (ffmpegMsg.Contains("Connection refused"))
                return $"Connection refused ({_transport.ToUpper()}) - device unreachable or port blocked";

            if (ffmpegMsg.Contains("Connection timed out") || ffmpegMsg.Contains("Timed out"))
                return $"Connection timed out after {_connectionTimeoutSec}s - check network and IP address";

            // Check 404 before 401 — some cameras return 401 for invalid paths
            if (ffmpegMsg.Contains("404") || ffmpegMsg.Contains("Not Found"))
                return "Stream not found (404) - check RTSP path";

            if (ffmpegMsg.Contains("401") || ffmpegMsg.Contains("Unauthorized"))
                return "Authentication failed (401) - check username/password and RTSP path (some cameras return 401 for invalid paths)";

            if (ffmpegMsg.Contains("403") || ffmpegMsg.Contains("Forbidden"))
                return "Access forbidden (403) - user lacks permission";

            if (ffmpegMsg.Contains("453") || ffmpegMsg.Contains("Not Enough Bandwidth"))
                return "Not enough bandwidth (453) - too many streams or reduce resolution";

            if (ffmpegMsg.Contains("Name or service not known") || ffmpegMsg.Contains("resolve"))
                return "DNS resolution failed - cannot resolve hostname";

            if (ffmpegMsg.Contains("Network is unreachable"))
                return "Network unreachable - check network connectivity";

            return $"Connection failed: {ffmpegMsg}";
        }

        private static volatile bool _ffmpegPathSet;

        private static void SetupFfmpegPath()
        {
            if (_ffmpegPathSet) return;
            _ffmpegPathSet = true;

            var asmDir = Path.GetDirectoryName(typeof(RtspClientWorker).Assembly.Location);
            var x64Dir = Path.Combine(asmDir, "x64");
            if (Directory.Exists(x64Dir))
                ffmpeg.RootPath = x64Dir;
        }

        /// <summary>
        /// Check if audio data already starts with ADTS sync word (0xFFF).
        /// </summary>
        private static bool StartsWithAdtsSync(byte[] data)
        {
            return data != null && data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xF0) == 0xF0;
        }

        /// <summary>
        /// Build a 7-byte ADTS header template from AAC AudioSpecificConfig (extradata).
        /// The frame length fields must be patched per-packet.
        /// </summary>
        private static byte[] BuildAdtsTemplate(byte[] asc)
        {
            // Parse AudioSpecificConfig
            // Bits: [4:0] audioObjectType, [3:0] samplingFrequencyIndex, [3:0] channelConfiguration
            int objectType = (asc[0] >> 3) & 0x1F;
            int freqIndex = ((asc[0] & 0x07) << 1) | ((asc[1] >> 7) & 0x01);
            int chanConfig = (asc[1] >> 3) & 0x0F;

            // ADTS profile = objectType - 1 (ADTS uses 0-based, ASC uses 1-based)
            int profile = objectType - 1;

            var hdr = new byte[7];
            hdr[0] = 0xFF;                                                       // Sync word high
            hdr[1] = 0xF1;                                                       // Sync word low + MPEG-4 + Layer 0 + no CRC
            hdr[2] = (byte)((profile << 6) | (freqIndex << 2) | ((chanConfig >> 2) & 0x01)); // Profile + freq + channel high
            hdr[3] = (byte)((chanConfig & 0x03) << 6);                           // Channel low + frame length will be patched
            hdr[4] = 0;                                                           // Frame length mid — patched per packet
            hdr[5] = 0x1F;                                                        // Frame length low + buffer fullness high
            hdr[6] = 0xFC;                                                        // Buffer fullness low + 0 raw frames
            return hdr;
        }

        /// <summary>
        /// Map FFmpeg audio codec ID to Milestone AudioCodecType and subtype.
        /// Values match VideoOS.Platform.DriverFramework.Data.AudioCodecType constants.
        /// </summary>
        private static (uint codecType, uint codecSubtype) MapAudioCodec(AVCodecID codecId)
        {
            switch (codecId)
            {
                case AVCodecID.AV_CODEC_ID_AAC:
                    return (18, 0); // AACMPEG4
                case AVCodecID.AV_CODEC_ID_PCM_MULAW:
                    return (3, 1);  // G711, uLaw
                case AVCodecID.AV_CODEC_ID_PCM_ALAW:
                    return (3, 2);  // G711, ALaw
                case AVCodecID.AV_CODEC_ID_PCM_S16LE:
                case AVCodecID.AV_CODEC_ID_PCM_S16BE:
                case AVCodecID.AV_CODEC_ID_PCM_U8:
                    return (1, 0);  // PCM
                case AVCodecID.AV_CODEC_ID_ADPCM_G726:
                case AVCodecID.AV_CODEC_ID_ADPCM_G726LE:
                    return (9, 3);  // G726, 32bit LE
                default:
                    return (1, 0);  // Fallback to PCM
            }
        }

        private static string FfmpegError(int error)
        {
            var bufSize = 1024;
            var buf = stackalloc byte[bufSize];
            ffmpeg.av_strerror(error, buf, (ulong)bufSize);
            return Marshal.PtrToStringAnsi((IntPtr)buf);
        }
    }

    internal enum RtspWorkerState
    {
        Idle,
        Connecting,
        AwaitingKeyFrame,
        Streaming,
        Reconnecting,
        Error,
        NoVideoTrack,
        UnsupportedCodec,
        Disabled,
    }
}
