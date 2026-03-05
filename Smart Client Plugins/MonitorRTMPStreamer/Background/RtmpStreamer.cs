using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace MonitorRTMPStreamer.Background
{
    public unsafe class RtmpStreamer : IDisposable
    {
        private AVFormatContext* _formatCtx;
        private AVCodecContext* _codecCtx;
        private AVStream* _stream;
        private SwsContext* _swsCtx;
        private AVFrame* _frame;
        private AVPacket* _packet;
        private long _pts;
        private bool _headerWritten;
        private readonly int _width;
        private readonly int _height;
        private readonly string _rtmpUrl;
        private readonly Action<string> _log;
        private readonly Action<string> _logError;

        public bool IsRunning { get; private set; }

        public RtmpStreamer(int width, int height, string rtmpUrl,
            Action<string> log, Action<string> logError)
        {
            // Ensure even dimensions for H.264
            _width = width % 2 == 0 ? width : width - 1;
            _height = height % 2 == 0 ? height : height - 1;
            _rtmpUrl = rtmpUrl;
            _log = log;
            _logError = logError;
        }

        public bool Start()
        {
            try
            {
                SetupFfmpegPath();

                // Output format context for RTMP (FLV container)
                AVFormatContext* fmtCtx = null;
                ffmpeg.avformat_alloc_output_context2(&fmtCtx, null, "flv", _rtmpUrl);
                if (fmtCtx == null)
                {
                    _logError("Could not create FLV output context.");
                    return false;
                }
                _formatCtx = fmtCtx;

                // Find H.264 encoder
                var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
                if (codec == null)
                {
                    _logError("H.264 encoder not found.");
                    return false;
                }

                // Create stream
                _stream = ffmpeg.avformat_new_stream(_formatCtx, null);
                _stream->time_base = new AVRational { num = 1, den = 1 };

                // Setup encoder
                _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                _codecCtx->width = _width;
                _codecCtx->height = _height;
                _codecCtx->time_base = new AVRational { num = 1, den = 1 };
                _codecCtx->framerate = new AVRational { num = 1, den = 1 };
                _codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                _codecCtx->gop_size = 1; // All keyframes at 1 FPS
                _codecCtx->max_b_frames = 0;

                // Ultrafast preset + zerolatency
                ffmpeg.av_opt_set(_codecCtx->priv_data, "preset", "ultrafast", 0);
                ffmpeg.av_opt_set(_codecCtx->priv_data, "tune", "zerolatency", 0);

                if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                    _codecCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

                var ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                if (ret < 0)
                {
                    _logError($"Could not open H.264 encoder: {FfmpegError(ret)}");
                    return false;
                }

                ffmpeg.avcodec_parameters_from_context(_stream->codecpar, _codecCtx);

                // Open RTMP connection
                ret = ffmpeg.avio_open(&_formatCtx->pb, _rtmpUrl, ffmpeg.AVIO_FLAG_WRITE);
                if (ret < 0)
                {
                    _logError($"Could not open RTMP: {FfmpegError(ret)}");
                    return false;
                }

                ret = ffmpeg.avformat_write_header(_formatCtx, null);
                if (ret < 0)
                {
                    _logError($"Could not write FLV header: {FfmpegError(ret)}");
                    return false;
                }
                _headerWritten = true;

                // Allocate frame + packet
                _frame = ffmpeg.av_frame_alloc();
                _frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
                _frame->width = _width;
                _frame->height = _height;
                ffmpeg.av_frame_get_buffer(_frame, 0);

                _packet = ffmpeg.av_packet_alloc();

                // Setup scaler: BGRA -> YUV420P
                _swsCtx = ffmpeg.sws_getContext(
                    _width, _height, AVPixelFormat.AV_PIX_FMT_BGRA,
                    _width, _height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                    (int)SwsFlags.SWS_FAST_BILINEAR, null, null, null);

                _pts = 0;
                IsRunning = true;
                _log($"RTMP stream started: {_rtmpUrl} ({_width}x{_height})");
                return true;
            }
            catch (Exception ex)
            {
                _logError($"RTMP start failed: {ex.Message}");
                return false;
            }
        }

        public void PushFrame(Bitmap frame)
        {
            if (!IsRunning) return;

            Bitmap resized = null;
            try
            {
                // Resize if dimensions don't match
                if (frame.Width != _width || frame.Height != _height)
                {
                    resized = new Bitmap(_width, _height);
                    using (var g = Graphics.FromImage(resized))
                        g.DrawImage(frame, 0, 0, _width, _height);
                }

                var src = resized ?? frame;
                var rect = new Rectangle(0, 0, src.Width, src.Height);
                var bmpData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    // Convert BGRA -> YUV420P
                    ffmpeg.av_frame_make_writable(_frame);
                    var srcData = new byte*[] { (byte*)bmpData.Scan0 };
                    var srcLinesize = new int[] { bmpData.Stride };
                    var dstData = new byte*[] { _frame->data[0], _frame->data[1], _frame->data[2], _frame->data[3] };
                    var dstLinesize = new int[] { _frame->linesize[0], _frame->linesize[1], _frame->linesize[2], _frame->linesize[3] };

                    ffmpeg.sws_scale(_swsCtx, srcData, srcLinesize, 0, _height,
                        dstData, dstLinesize);

                    _frame->pts = _pts++;

                    // Encode
                    var ret = ffmpeg.avcodec_send_frame(_codecCtx, _frame);
                    if (ret < 0) return;

                    while (ret >= 0)
                    {
                        ret = ffmpeg.avcodec_receive_packet(_codecCtx, _packet);
                        if (ret < 0) break;

                        _packet->stream_index = _stream->index;
                        ffmpeg.av_packet_rescale_ts(_packet, _codecCtx->time_base, _stream->time_base);

                        ret = ffmpeg.av_interleaved_write_frame(_formatCtx, _packet);
                        ffmpeg.av_packet_unref(_packet);

                        if (ret < 0)
                        {
                            _logError($"RTMP write error: {FfmpegError(ret)}");
                            IsRunning = false;
                            return;
                        }
                    }
                }
                finally
                {
                    src.UnlockBits(bmpData);
                }
            }
            catch (Exception ex)
            {
                _logError($"RTMP push error: {ex.Message}");
                IsRunning = false;
            }
            finally
            {
                resized?.Dispose();
            }
        }

        public void Dispose()
        {
            IsRunning = false;

            if (_headerWritten && _formatCtx != null)
            {
                try { ffmpeg.av_write_trailer(_formatCtx); } catch { }
            }

            if (_swsCtx != null)
            {
                ffmpeg.sws_freeContext(_swsCtx);
                _swsCtx = null;
            }

            if (_packet != null)
            {
                var pkt = _packet;
                ffmpeg.av_packet_free(&pkt);
                _packet = null;
            }

            if (_frame != null)
            {
                var frm = _frame;
                ffmpeg.av_frame_free(&frm);
                _frame = null;
            }

            if (_codecCtx != null)
            {
                var ctx = _codecCtx;
                ffmpeg.avcodec_free_context(&ctx);
                _codecCtx = null;
            }

            if (_formatCtx != null)
            {
                if (_formatCtx->pb != null)
                    ffmpeg.avio_closep(&_formatCtx->pb);
                ffmpeg.avformat_free_context(_formatCtx);
                _formatCtx = null;
            }
        }

        private static void SetupFfmpegPath()
        {
            // FFmpeg.GPL NuGet copies DLLs to x64\ subfolder in output
            var asmDir = Path.GetDirectoryName(typeof(RtmpStreamer).Assembly.Location);
            var x64Dir = Path.Combine(asmDir, "x64");
            if (Directory.Exists(x64Dir))
                ffmpeg.RootPath = x64Dir;
        }

        private static string FfmpegError(int error)
        {
            var bufSize = 1024;
            var buf = stackalloc byte[bufSize];
            ffmpeg.av_strerror(error, buf, (ulong)bufSize);
            return Marshal.PtrToStringAnsi((IntPtr)buf);
        }
    }
}
