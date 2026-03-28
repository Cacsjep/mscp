using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Timelapse.Services
{
    /// <summary>
    /// Encodes frames into an MP4 file using FFmpeg H.264.
    /// Adapted from MonitorRTMPStreamer's RtmpStreamer pattern.
    /// </summary>
    internal unsafe class TimelapseEncoder : IDisposable
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
        private readonly int _fps;
        private readonly string _outputPath;
        private readonly Action<string> _log;

        public TimelapseEncoder(int width, int height, int fps, string outputPath, Action<string> log)
        {
            _width = width % 2 == 0 ? width : width - 1;
            _height = height % 2 == 0 ? height : height - 1;
            _fps = Math.Max(1, Math.Min(fps, 60));
            _outputPath = outputPath;
            _log = log;
        }

        public bool Start()
        {
            try
            {
                SetupFfmpegPath();

                AVFormatContext* fmtCtx = null;
                ffmpeg.avformat_alloc_output_context2(&fmtCtx, null, "mp4", _outputPath);
                if (fmtCtx == null)
                {
                    _log("Could not create MP4 output context.");
                    return false;
                }
                _formatCtx = fmtCtx;

                var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
                if (codec == null)
                {
                    _log("H.264 encoder not found.");
                    return false;
                }

                _stream = ffmpeg.avformat_new_stream(_formatCtx, null);
                _stream->time_base = new AVRational { num = 1, den = _fps };

                _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                _codecCtx->width = _width;
                _codecCtx->height = _height;
                _codecCtx->time_base = new AVRational { num = 1, den = _fps };
                _codecCtx->framerate = new AVRational { num = _fps, den = 1 };
                _codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                _codecCtx->gop_size = _fps * 2;
                _codecCtx->max_b_frames = 2;

                // Medium preset for offline encoding - better quality than ultrafast
                ffmpeg.av_opt_set(_codecCtx->priv_data, "preset", "medium", 0);
                ffmpeg.av_opt_set(_codecCtx->priv_data, "crf", "23", 0);

                if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                    _codecCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

                var ret = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                if (ret < 0)
                {
                    _log($"Could not open H.264 encoder: {FfmpegError(ret)}");
                    return false;
                }

                ffmpeg.avcodec_parameters_from_context(_stream->codecpar, _codecCtx);

                ret = ffmpeg.avio_open(&_formatCtx->pb, _outputPath, ffmpeg.AVIO_FLAG_WRITE);
                if (ret < 0)
                {
                    _log($"Could not open output file: {FfmpegError(ret)}");
                    return false;
                }

                ret = ffmpeg.avformat_write_header(_formatCtx, null);
                if (ret < 0)
                {
                    _log($"Could not write MP4 header: {FfmpegError(ret)}");
                    return false;
                }
                _headerWritten = true;

                _frame = ffmpeg.av_frame_alloc();
                _frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
                _frame->width = _width;
                _frame->height = _height;
                ffmpeg.av_frame_get_buffer(_frame, 0);

                _packet = ffmpeg.av_packet_alloc();

                _swsCtx = ffmpeg.sws_getContext(
                    _width, _height, AVPixelFormat.AV_PIX_FMT_BGRA,
                    _width, _height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                    (int)SwsFlags.SWS_BILINEAR, null, null, null);

                _pts = 0;
                return true;
            }
            catch (Exception ex)
            {
                _log($"Encoder start failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Encode a single bitmap frame. The bitmap must be ARGB/BGRA format.
        /// </summary>
        public bool PushFrame(Bitmap frame)
        {
            Bitmap resized = null;
            try
            {
                if (frame.Width != _width || frame.Height != _height)
                {
                    resized = new Bitmap(_width, _height);
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(frame, 0, 0, _width, _height);
                    }
                }

                var src = resized ?? frame;
                var rect = new Rectangle(0, 0, src.Width, src.Height);
                var bmpData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    ffmpeg.av_frame_make_writable(_frame);
                    var srcData = new byte*[] { (byte*)bmpData.Scan0 };
                    var srcLinesize = new int[] { bmpData.Stride };
                    var dstData = new byte*[] { _frame->data[0], _frame->data[1], _frame->data[2], _frame->data[3] };
                    var dstLinesize = new int[] { _frame->linesize[0], _frame->linesize[1], _frame->linesize[2], _frame->linesize[3] };

                    ffmpeg.sws_scale(_swsCtx, srcData, srcLinesize, 0, _height, dstData, dstLinesize);
                    _frame->pts = _pts++;

                    var ret = ffmpeg.avcodec_send_frame(_codecCtx, _frame);
                    if (ret < 0) return false;

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
                            _log($"Write error: {FfmpegError(ret)}");
                            return false;
                        }
                    }
                    return true;
                }
                finally
                {
                    src.UnlockBits(bmpData);
                }
            }
            catch (Exception ex)
            {
                _log($"Encode error: {ex.Message}");
                return false;
            }
            finally
            {
                resized?.Dispose();
            }
        }

        /// <summary>
        /// Flush remaining frames and finalize the MP4 file.
        /// </summary>
        public void Finish()
        {
            if (_codecCtx != null)
            {
                // Flush encoder
                ffmpeg.avcodec_send_frame(_codecCtx, null);
                while (true)
                {
                    var ret = ffmpeg.avcodec_receive_packet(_codecCtx, _packet);
                    if (ret < 0) break;
                    _packet->stream_index = _stream->index;
                    ffmpeg.av_packet_rescale_ts(_packet, _codecCtx->time_base, _stream->time_base);
                    ffmpeg.av_interleaved_write_frame(_formatCtx, _packet);
                    ffmpeg.av_packet_unref(_packet);
                }
            }

            if (_headerWritten && _formatCtx != null)
            {
                try { ffmpeg.av_write_trailer(_formatCtx); } catch { }
            }
        }

        public void Dispose()
        {
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
            var asmDir = Path.GetDirectoryName(typeof(TimelapseEncoder).Assembly.Location);
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
