using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;

namespace LowLatencyVideoWall.Render
{
    /// <summary>Where one pane's RTSP session has got to.</summary>
    public enum PaneState
    {
        Idle,
        Connecting,
        AwaitingKeyFrame,
        Streaming,
        Reconnecting,
        Error
    }

    /// <summary>
    /// Time to first frame for one pane, broken into its phases. On a wall this matters more
    /// than on a single view: 48 cameras starting at once is the worst case for every phase,
    /// and the only way to see which phase went wrong is to time them separately per camera.
    /// </summary>
    internal sealed class PaneStartupTiming
    {
        /// <summary>Stopwatch timestamp the session clock started, so the render thread can
        /// stamp its own first blit against the same origin.</summary>
        internal long SessionStartTicks;

        internal double OpenInputMs;        // TCP connect + DESCRIBE + SETUP + PLAY
        internal double StreamInfoMs;       // avformat_find_stream_info, usually skipped
        internal double DecoderOpenMs;
        internal double FirstPacketMs;
        internal double FirstKeyFrameMs;
        internal double FirstDecodedFrameMs;
        internal double FirstBlitMs;        // filled in by the render thread

        internal void Reset()
        {
            SessionStartTicks = 0;
            OpenInputMs = StreamInfoMs = DecoderOpenMs = 0;
            FirstPacketMs = FirstKeyFrameMs = FirstDecodedFrameMs = FirstBlitMs = 0;
        }
    }

    /// <summary>
    /// What the live session turned out to be. Written once per session during setup, read by
    /// the UI timer. x64 only, so the doubles cannot tear.
    /// </summary>
    internal sealed class PaneSessionInfo
    {
        internal volatile string DecoderName = "";
        internal volatile string PixelFormatName = "";
        internal volatile bool IsHardware;
        internal volatile int Width;
        internal volatile int Height;
        internal double SourceFps;
        internal volatile string Profile = "";

        /// <summary>Codec and profile only, short enough for a pane overlay line.</summary>
        internal volatile string Codec = "";

        internal volatile bool UsedStreamProbe;

        /// <summary>
        /// Frames of decoder output delay forced by the bitstream. Anything above zero means
        /// B-frames, which hold every frame back by that many frames before it can be shown.
        /// </summary>
        internal volatile int ReorderDelayFrames;
    }

    /// <summary>
    /// Pulls H.264 off one camera's RTSP URL and decodes it on D3D11VA, on the same
    /// ID3D11Device the wall presents with, so decoded surfaces never leave the GPU.
    ///
    /// One of these per pane, each on its own thread. The decode path itself is the one proven
    /// in the single camera prototype; what changed for the wall is only what has to
    /// change when there are 48 of them:
    ///
    ///   - every session gets its own FFmpeg hardware device context over the shared
    ///     ID3D11Device, so tearing one pane down frees that pane's surface pool and cannot
    ///     disturb the other 47. They all serialise on the same device lock either way,
    ///     because av_hwdevice_ctx_init locks through ID3D11Multithread.
    ///   - extra_hw_frames is as small as the renderer can live with, because the pool is
    ///     per pane and 48 pools of oversized NV12 surfaces is real video memory.
    ///   - reconnects back off and are jittered, so a recording server or switch outage does
    ///     not turn into 48 synchronised reconnect storms.
    ///   - the demuxer gets an interrupt callback, so Stop returns in milliseconds instead of
    ///     waiting out a five second socket timeout 48 times over.
    /// </summary>
    internal unsafe sealed class CameraVideoSource : IDisposable
    {
        private static bool _ffmpegPathSet;
        private static readonly object _pathLock = new object();

        // libavcodec hwconfig method bits. FFmpeg.AutoGen does not surface these constants.
        private const int HwConfigMethodHwDeviceCtx = 0x01;
        private const int HwConfigMethodHwFramesCtx = 0x02;

        private readonly string _url;
        private readonly string _transport;
        private readonly Func<IntPtr> _acquireNativeDevice;
        private readonly FrameSlot _slot;
        private readonly PaneStats _stats;
        private readonly Action<string> _log;

        /// <summary>Spread of the reconnect delay for this camera, in milliseconds.</summary>
        private readonly int _reconnectJitterMs;

        // Held for the life of the object: the native side keeps only a function pointer, so
        // nothing else would stop the delegate being collected mid-session.
        private readonly AVIOInterruptCB_callback _interrupt;

        /// <summary>
        /// TickCount of the last sign of life from the network side. The interrupt callback
        /// compares against it, which is the only reliable way out of a blocking read.
        /// </summary>
        private volatile int _lastIoProgressTicks;

        private Thread _thread;
        private CancellationTokenSource _cts;

        private AVBufferRef* _hwDeviceCtx;
        private AVFrame* _receiveFrame;

        private int _state = (int)PaneState.Idle;

        internal PaneState State
        {
            get => (PaneState)Volatile.Read(ref _state);
            private set => Volatile.Write(ref _state, (int)value);
        }

        internal volatile string LastError;
        internal PaneStartupTiming Timing { get; } = new PaneStartupTiming();
        internal PaneSessionInfo Info { get; } = new PaneSessionInfo();

        internal string Url => _url;

        internal CameraVideoSource(string url, string transport, Func<IntPtr> acquireNativeDevice,
            FrameSlot slot, PaneStats stats, Action<string> log, int reconnectJitterMs)
        {
            _url = url;
            _transport = (transport ?? "tcp").ToLowerInvariant();
            _acquireNativeDevice = acquireNativeDevice;
            _slot = slot;
            _stats = stats;
            _log = log;
            _reconnectJitterMs = Math.Max(0, reconnectJitterMs);
            _interrupt = InterruptCheck;
        }

        /// <summary>The URL with any credentials masked, safe to show and to log.</summary>
        internal static string MaskUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                if (!string.IsNullOrEmpty(uri.UserInfo))
                    return url.Replace(uri.UserInfo + "@", "***@");
            }
            catch { }
            return url;
        }

        internal void Start(string threadName)
        {
            if (_thread != null) return;

            _cts = new CancellationTokenSource();
            _thread = new Thread(WorkerLoop)
            {
                // Named per camera: with 48 of these, a thread dump is only useful if the
                // threads say which camera they belong to.
                Name = threadName,
                IsBackground = true,

                // Above normal, but not higher: the single render thread has to be able to
                // outrun 48 decode threads or the wall stops being paced by the compositor.
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }

        /// <summary>
        /// Asks the session to stop. Returns immediately; the interrupt callback unblocks the
        /// demuxer and the thread winds itself up. Call <see cref="Join"/> to wait for it.
        /// </summary>
        internal void RequestStop() => _cts?.Cancel();

        internal void Join(int timeoutMs)
        {
            var thread = _thread;
            _thread = null;
            if (thread != null && !thread.Join(timeoutMs))
                _log($"Decode thread did not stop within {timeoutMs} ms");

            State = PaneState.Idle;
        }

        internal void Stop()
        {
            RequestStop();
            Join(4000);
        }

        private bool Cancelled => _cts == null || _cts.IsCancellationRequested;

        /// <summary>
        /// A read that has produced nothing for this long is treated as dead. Slightly above
        /// the five second socket timeout, so when the socket options are honoured they are
        /// what ends the session and this is only the backstop.
        /// </summary>
        private const int IoStallMs = 6000;

        /// <summary>
        /// Non zero tells libavformat to abandon whatever blocking I/O it is in. Without this
        /// a Stop during connect waits out the five second socket timeout, which is fine for
        /// one camera and unacceptable when a view switch replaces all 48.
        ///
        /// It is also the only dependable read watchdog. The RTSP socket timeout is spelled
        /// "stimeout" in older FFmpeg and "timeout" in newer, and "rw_timeout" only reaches
        /// the AVIO layer, which RTSP does not use for its media sockets. Set the wrong one,
        /// or build against a version that dropped it, and a camera that holds its TCP
        /// connection open while sending nothing parks av_read_frame forever: the pane never
        /// errors, never reconnects and never comes back. Timing it here does not care which
        /// option name the build happens to honour.
        /// </summary>
        private int InterruptCheck(void* opaque)
        {
            if (Cancelled) return 1;
            return unchecked(Environment.TickCount - _lastIoProgressTicks) > IoStallMs ? 1 : 0;
        }

        /// <summary>The network side did something. Resets the stall watchdog.</summary>
        private void MarkIoProgress() => _lastIoProgressTicks = Environment.TickCount;

        private void WorkerLoop()
        {
            SetupFfmpegPath();

            var attempt = 0;
            while (!Cancelled)
            {
                attempt++;
                State = PaneState.Connecting;
                LastError = null;
                Timing.Reset();
                _stats.ResetSession();

                var delivered = false;
                try
                {
                    delivered = RunSession();
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    _log($"Session error: {ex.GetType().Name}: {ex.Message}");
                }

                if (Cancelled) break;

                // A session that actually ran is not a failed attempt. Without this the
                // backoff only ever grows, so a camera that drops once an hour ends up
                // waiting the full ten seconds to come back for the rest of the day.
                if (delivered) attempt = 0;

                State = PaneState.Reconnecting;

                var delay = ReconnectDelayMs(attempt);
                _log($"Disconnected ({LastError ?? "no error reported"}), " +
                     $"reconnecting in {delay} ms (attempt {attempt})");

                // Slept in slices so cancellation is still prompt during a long backoff.
                for (var waited = 0; waited < delay && !Cancelled; waited += 100)
                    Thread.Sleep(100);
            }

            ReleaseHwDeviceContext();
            State = PaneState.Idle;
        }

        /// <summary>
        /// Backoff plus a per camera offset. 48 cameras behind one switch all fail at the same
        /// instant, and without the offset they would then all retry at the same instant for
        /// as long as the outage lasts.
        /// </summary>
        private int ReconnectDelayMs(int attempt)
        {
            var baseDelay = attempt <= 1 ? 1000 : Math.Min(10000, 1000 * attempt);
            return baseDelay + _reconnectJitterMs;
        }

        /// <summary>Runs one connection to exhaustion. True if it ever reached Streaming.</summary>
        private bool RunSession()
        {
            AVFormatContext* fmtCtx = null;
            AVCodecContext* codecCtx = null;
            AVPacket* pkt = null;

            var streamed = false;
            var clock = Stopwatch.StartNew();
            Timing.SessionStartTicks = Stopwatch.GetTimestamp();
            MarkIoProgress();

            try
            {
                fmtCtx = ffmpeg.avformat_alloc_context();
                if (fmtCtx == null)
                {
                    LastError = "avformat_alloc_context failed";
                    State = PaneState.Error;
                    return false;
                }

                fmtCtx->interrupt_callback = new AVIOInterruptCB
                {
                    callback = _interrupt,
                    opaque = null
                };

                AVDictionary* opts = null;
                BuildOpenOptions(&opts);

                var ret = ffmpeg.avformat_open_input(&fmtCtx, _url, null, &opts);
                Timing.OpenInputMs = clock.Elapsed.TotalMilliseconds;
                MarkIoProgress();
                if (opts != null) ffmpeg.av_dict_free(&opts);

                if (ret < 0)
                {
                    LastError = $"Could not open {MaskUrl(_url)}: {Describe(ret)}";
                    State = PaneState.Error;
                    return false;
                }

                // RTSP hands us codec parameters and SPS/PPS from the SDP during
                // avformat_open_input, so the probe is usually pure startup cost. Only pay for
                // it when the SDP did not tell us enough to open a decoder. Measured at about
                // 160 ms on one camera, which is 160 ms times 48 on a wall.
                var videoStream = FindVideoStream(fmtCtx);
                if (!IsUsable(fmtCtx, videoStream))
                {
                    ret = ffmpeg.avformat_find_stream_info(fmtCtx, null);
                    if (ret < 0)
                    {
                        LastError = $"No stream info: {Describe(ret)}";
                        State = PaneState.Error;
                        return false;
                    }
                    videoStream = FindVideoStream(fmtCtx);
                    Info.UsedStreamProbe = true;
                }

                Timing.StreamInfoMs = clock.Elapsed.TotalMilliseconds;
                MarkIoProgress();

                if (videoStream < 0)
                {
                    LastError = "No video stream in the RTSP source";
                    State = PaneState.Error;
                    return false;
                }

                var stream = fmtCtx->streams[videoStream];
                var codecpar = stream->codecpar;

                Info.Width = codecpar->width;
                Info.Height = codecpar->height;
                Info.SourceFps = stream->avg_frame_rate.den > 0
                    ? ffmpeg.av_q2d(stream->avg_frame_rate)
                    : ffmpeg.av_q2d(stream->r_frame_rate);

                codecCtx = OpenDecoder(codecpar, clock);
                if (codecCtx == null)
                {
                    State = PaneState.Error;
                    return false;
                }

                var geometry = Info.Width > 0 ? $"{Info.Width}x{Info.Height}" : "size from SPS";
                _log($"Connected {MaskUrl(_url)} {geometry} {Info.SourceFps:0.##} fps " +
                     $"transport={_transport} decoder={Info.DecoderName} " +
                     $"probe={(Info.UsedStreamProbe ? "yes" : "skipped")} " +
                     $"open={Timing.OpenInputMs:0}ms info={Timing.StreamInfoMs:0}ms");

                State = PaneState.AwaitingKeyFrame;

                _receiveFrame = ffmpeg.av_frame_alloc();
                pkt = ffmpeg.av_packet_alloc();

                var gotKeyFrame = false;

                MarkIoProgress();

                while (!Cancelled)
                {
                    ret = ffmpeg.av_read_frame(fmtCtx, pkt);
                    if (ret < 0)
                    {
                        LastError = ret == ffmpeg.AVERROR_EOF
                            ? "Stream ended (EOF)"
                            : $"Read error: {Describe(ret)}";
                        break;
                    }

                    var arrival = Stopwatch.GetTimestamp();
                    MarkIoProgress();

                    try
                    {
                        if (pkt->stream_index != videoStream) continue;

                        var isKey = (pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                        _stats.AddVideoPacket(pkt->size, isKey);

                        if (Timing.FirstPacketMs == 0)
                            Timing.FirstPacketMs = clock.Elapsed.TotalMilliseconds;

                        if (!gotKeyFrame)
                        {
                            // Feeding the decoder mid GOP just produces errors until the
                            // first IDR, so the wait here is however long the camera's own
                            // GOP leaves before its next keyframe.
                            if (!isKey) continue;
                            gotKeyFrame = true;
                            Timing.FirstKeyFrameMs = clock.Elapsed.TotalMilliseconds;
                            State = PaneState.Streaming;
                            streamed = true;
                        }

                        DecodePacket(codecCtx, pkt, arrival, clock);
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(pkt);
                    }
                }
            }
            finally
            {
                if (pkt != null) ffmpeg.av_packet_free(&pkt);
                if (_receiveFrame != null)
                {
                    var f = _receiveFrame;
                    ffmpeg.av_frame_free(&f);
                    _receiveFrame = null;
                }
                if (codecCtx != null) ffmpeg.avcodec_free_context(&codecCtx);
                if (fmtCtx != null) ffmpeg.avformat_close_input(&fmtCtx);

                _slot.Clear();
            }

            return streamed;
        }

        private void BuildOpenOptions(AVDictionary** opts)
        {
            if (_transport == "tcp" || _transport == "udp" || _transport == "udp_multicast")
                ffmpeg.av_dict_set(opts, "rtsp_transport", _transport, 0);
            // "auto" leaves FFmpeg to choose, which means UDP first.

            // All three spellings. "stimeout" is what older FFmpeg called the RTSP socket
            // timeout, "timeout" is what newer FFmpeg calls it, and "rw_timeout" covers the
            // AVIO layer. An option this build does not know is left in the dictionary and
            // ignored, so setting all three costs nothing and keeps the behaviour from
            // changing silently on an FFmpeg upgrade. The interrupt callback is still the
            // backstop, because none of them is guaranteed to be honoured.
            const long timeoutUs = 5_000_000L;
            ffmpeg.av_dict_set(opts, "stimeout", timeoutUs.ToString(), 0);
            ffmpeg.av_dict_set(opts, "timeout", timeoutUs.ToString(), 0);
            ffmpeg.av_dict_set(opts, "rw_timeout", timeoutUs.ToString(), 0);

            // Latency knobs. nobuffer and low_delay stop the demuxer and decoder holding
            // frames back for reordering, max_delay caps RTP reordering, and the small probe
            // limits keep avformat_find_stream_info from spending a second measuring a frame
            // rate we do not need it to measure.
            ffmpeg.av_dict_set(opts, "fflags", "nobuffer", 0);
            ffmpeg.av_dict_set(opts, "flags", "low_delay", 0);
            ffmpeg.av_dict_set(opts, "max_delay", "100000", 0);
            ffmpeg.av_dict_set(opts, "reorder_queue_size", "0", 0);
            ffmpeg.av_dict_set(opts, "probesize", "65536", 0);
            ffmpeg.av_dict_set(opts, "analyzeduration", "100000", 0);
        }

        private AVCodecContext* OpenDecoder(AVCodecParameters* codecpar, Stopwatch clock)
        {
            var codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
            if (codec == null)
            {
                LastError = $"No decoder for {ffmpeg.avcodec_get_name(codecpar->codec_id)}";
                return null;
            }

            var ctx = ffmpeg.avcodec_alloc_context3(codec);
            if (ctx == null)
            {
                LastError = "avcodec_alloc_context3 failed";
                return null;
            }

            var ret = ffmpeg.avcodec_parameters_to_context(ctx, codecpar);
            if (ret < 0)
            {
                LastError = $"avcodec_parameters_to_context failed: {Describe(ret)}";
                ffmpeg.avcodec_free_context(&ctx);
                return null;
            }

            ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            ctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            var wantHardware = false;
            var hw = EnsureHwDeviceContext();
            if (hw != null)
            {
                // With hw_device_ctx set, avcodec_default_get_format picks the hardware pixel
                // format and builds the frames context itself, so no get_format callback is
                // needed and nothing has to be marshalled per frame.
                ctx->hw_device_ctx = ffmpeg.av_buffer_ref(hw);

                // The render thread holds at most two frames per pane outside the decoder
                // pool: the one it is drawing and the one sitting in the slot. Four covers
                // that with margin. The prototype used six because it only had one pool to
                // pay for; here there are up to 48.
                ctx->extra_hw_frames = 4;
                wantHardware = true;
            }

            if (!wantHardware)
            {
                // Software decode is not a wall path, but a pane that falls back is better
                // than a pane that is black. Slice threading only: frame threading would add
                // a frame of output delay, which is the opposite of the point.
                ctx->thread_type = ffmpeg.FF_THREAD_SLICE;
                ctx->thread_count = 2;
            }

            ret = ffmpeg.avcodec_open2(ctx, codec, null);
            Timing.DecoderOpenMs = clock.Elapsed.TotalMilliseconds;
            if (ret < 0)
            {
                LastError = $"avcodec_open2 failed: {Describe(ret)}";
                ffmpeg.avcodec_free_context(&ctx);
                return null;
            }

            Info.DecoderName = Utf8(codec->name) + (wantHardware ? " + d3d11va" : " (software)");
            Info.ReorderDelayFrames = ctx->has_b_frames;

            if (ctx->has_b_frames > 0)
                _log($"Bitstream carries B-frames: {ctx->has_b_frames} frame(s) of forced output " +
                     "delay. Turning B-frames off at the encoder would cut that from the latency.");

            return ctx;
        }

        private static int FindVideoStream(AVFormatContext* fmtCtx)
        {
            for (var i = 0; i < (int)fmtCtx->nb_streams; i++)
            {
                if (fmtCtx->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// True when the SDP already gave us enough to open a decoder. Dimensions are not
        /// required: Axis (and most cameras) put SPS/PPS in the SDP as sprop-parameter-sets,
        /// and the decoder reads the frame size out of the SPS itself. Insisting on codecpar
        /// width and height would force a probe that costs around 160 ms of startup.
        /// </summary>
        private static bool IsUsable(AVFormatContext* fmtCtx, int videoStream)
        {
            if (videoStream < 0) return false;
            var codecpar = fmtCtx->streams[videoStream]->codecpar;

            if (codecpar->codec_id == AVCodecID.AV_CODEC_ID_NONE) return false;
            if (codecpar->width > 0 && codecpar->height > 0) return true;

            return codecpar->extradata != null && codecpar->extradata_size > 0;
        }

        private static string ProfileName(AVCodecID codecId, int profile)
        {
            if (codecId != AVCodecID.AV_CODEC_ID_H264) return $"profile {profile}";

            switch (profile)
            {
                case 66: return "Baseline";
                case 77: return "Main";
                case 88: return "Extended";
                case 100: return "High";
                case 110: return "High 10";
                case 122: return "High 4:2:2";
                case 244: return "High 4:4:4";
                default: return $"profile {profile}";
            }
        }

        private void DecodePacket(AVCodecContext* ctx, AVPacket* pkt, long arrivalTicks, Stopwatch clock)
        {
            var started = Stopwatch.GetTimestamp();

            var ret = ffmpeg.avcodec_send_packet(ctx, pkt);
            if (ret < 0 && ret != ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                _stats.AddDecodeError();
                return;
            }

            while (true)
            {
                ret = ffmpeg.avcodec_receive_frame(ctx, _receiveFrame);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                    return;

                if (ret < 0)
                {
                    _stats.AddDecodeError();
                    return;
                }

                var elapsed = Stopwatch.GetTimestamp() - started;
                _stats.AddDecodedFrame(elapsed);

                if (Timing.FirstDecodedFrameMs == 0)
                {
                    Timing.FirstDecodedFrameMs = clock.Elapsed.TotalMilliseconds;
                    PublishFormat(ctx, _receiveFrame);
                }

                // Hand the references over to a frame the render thread owns and leave
                // _receiveFrame clean for the next call. No pixel data is copied.
                var outFrame = ffmpeg.av_frame_alloc();
                if (outFrame == null)
                {
                    ffmpeg.av_frame_unref(_receiveFrame);
                    return;
                }

                ffmpeg.av_frame_move_ref(outFrame, _receiveFrame);

                _slot.Push(new DecodedFrame
                {
                    Frame = outFrame,
                    ArrivalTicks = arrivalTicks,
                    IsHardware = outFrame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11
                });

                // If this packet yields more than one frame, time each one on its own.
                started = Stopwatch.GetTimestamp();
            }
        }

        private void PublishFormat(AVCodecContext* ctx, AVFrame* frame)
        {
            var fmt = (AVPixelFormat)frame->format;
            Info.PixelFormatName = ffmpeg.av_get_pix_fmt_name(fmt) ?? fmt.ToString();
            Info.IsHardware = fmt == AVPixelFormat.AV_PIX_FMT_D3D11;
            Info.Width = frame->width;
            Info.Height = frame->height;
            Info.ReorderDelayFrames = ctx->has_b_frames;

            // When the stream probe is skipped, codecpar carries no profile or level: both
            // only become known once the decoder has parsed the SPS, which is now.
            Info.Codec = $"{ffmpeg.avcodec_get_name(ctx->codec_id)} " +
                         $"{ProfileName(ctx->codec_id, ctx->profile)}";
            Info.Profile = $"{Info.Codec} level {ctx->level / 10.0:0.0}";

            if (!Info.IsHardware)
                _log($"D3D11VA was requested but the decoder returned {Info.PixelFormatName}. " +
                     "The wall only blits hardware surfaces, so this pane will stay black.");

            _log($"First frame {frame->width}x{frame->height} {Info.PixelFormatName} " +
                 $"ttff: open={Timing.OpenInputMs:0} info={Timing.StreamInfoMs:0} " +
                 $"firstPkt={Timing.FirstPacketMs:0} firstIDR={Timing.FirstKeyFrameMs:0} " +
                 $"decoded={Timing.FirstDecodedFrameMs:0} (ms)");
        }

        /// <summary>
        /// Wraps the wall's ID3D11Device in an FFmpeg hardware device context so this decoder
        /// allocates its surfaces on the very device the wall presents from. Sharing the device
        /// is what makes this zero copy: no cross device texture sharing, no readback.
        /// </summary>
        private AVBufferRef* EnsureHwDeviceContext()
        {
            if (_hwDeviceCtx != null) return _hwDeviceCtx;

            var devicePtr = _acquireNativeDevice == null ? IntPtr.Zero : _acquireNativeDevice();
            if (devicePtr == IntPtr.Zero)
            {
                _log("No D3D11 device available, hardware decode disabled for this session");
                return null;
            }

            var ctx = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
            if (ctx == null)
            {
                _log("av_hwdevice_ctx_alloc(d3d11va) failed");
                ReleaseComObject(devicePtr);
                return null;
            }

            var deviceCtx = (AVHWDeviceContext*)ctx->data;
            var d3d11 = (AVD3D11VADeviceContext*)deviceCtx->hwctx;

            // FFmpeg takes ownership of this reference and releases it when the context is
            // freed, which is why the caller handed us one it had already AddRef'd.
            d3d11->device = (ID3D11Device*)devicePtr.ToPointer();

            var ret = ffmpeg.av_hwdevice_ctx_init(ctx);
            if (ret < 0)
            {
                _log($"av_hwdevice_ctx_init(d3d11va) failed: {Describe(ret)}");
                ffmpeg.av_buffer_unref(&ctx);
                return null;
            }

            _hwDeviceCtx = ctx;
            return _hwDeviceCtx;
        }

        private void ReleaseHwDeviceContext()
        {
            if (_hwDeviceCtx == null) return;
            var ctx = _hwDeviceCtx;
            _hwDeviceCtx = null;
            ffmpeg.av_buffer_unref(&ctx);
        }

        /// <summary>
        /// What the decoder can do on this machine. Logged once at startup rather than per
        /// pane, since the answer is a property of the GPU and the FFmpeg build.
        /// </summary>
        internal static string DescribeHardwareConfigs()
        {
            SetupFfmpegPath();

            var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null) return "no h264 decoder";

            var parts = new List<string>();
            for (var i = 0; ; i++)
            {
                var cfg = ffmpeg.avcodec_get_hw_config(codec, i);
                if (cfg == null) break;

                var typeName = ffmpeg.av_hwdevice_get_type_name(cfg->device_type) ?? "?";
                var pixName = ffmpeg.av_get_pix_fmt_name(cfg->pix_fmt) ?? "?";
                var methods = cfg->methods;
                var how = (methods & HwConfigMethodHwDeviceCtx) != 0 ? "device_ctx"
                        : (methods & HwConfigMethodHwFramesCtx) != 0 ? "frames_ctx"
                        : "internal";

                parts.Add($"{typeName}/{pixName}:{how}");
            }
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        private static string Utf8(byte* text)
            => text == null ? "" : System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)text);

        private static string Describe(int error)
        {
            const int size = 256;
            var buffer = stackalloc byte[size];
            if (ffmpeg.av_strerror(error, buffer, size) < 0)
                return $"error {error}";
            return $"{Utf8(buffer)} ({error})";
        }

        /// <summary>
        /// Releases a COM reference we acquired but could not hand to FFmpeg. Calling
        /// IUnknown::Release through the vtable avoids taking a SharpDX dependency here.
        /// </summary>
        private static void ReleaseComObject(IntPtr unknown)
        {
            if (unknown == IntPtr.Zero) return;
            try { System.Runtime.InteropServices.Marshal.Release(unknown); }
            catch { }
        }

        internal static void SetupFfmpegPath()
        {
            lock (_pathLock)
            {
                if (_ffmpegPathSet) return;
                _ffmpegPathSet = true;

                // FFmpeg.GPL drops the native DLLs into an x64 subfolder of the output.
                var asmDir = Path.GetDirectoryName(typeof(CameraVideoSource).Assembly.Location);
                var x64Dir = Path.Combine(asmDir ?? ".", "x64");
                if (Directory.Exists(x64Dir))
                    ffmpeg.RootPath = x64Dir;

                // 48 sessions worth of libavcodec chatter would drown MIPLog.
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            }
        }

        public void Dispose()
        {
            Stop();
            ReleaseHwDeviceContext();
            _cts?.Dispose();
        }
    }
}
