using System;
using System.Threading;
using FFmpeg.AutoGen;

namespace LowLatencyVideoWall.Render
{
    /// <summary>
    /// A decoded frame on its way from one pane's decode thread to the single render thread.
    /// On the hardware path Frame->data[0] is an ID3D11Texture2D and Frame->data[1] is the
    /// slice index into that texture array, so nothing here is a pixel copy.
    /// </summary>
    internal unsafe struct DecodedFrame
    {
        public AVFrame* Frame;

        /// <summary>Stopwatch timestamp of the packet this frame came out of, for latency.</summary>
        public long ArrivalTicks;

        public bool IsHardware;
    }

    /// <summary>
    /// Single slot newest wins handoff, one per pane. The renderer is deliberately allowed to
    /// fall behind: showing the freshest frame matters more than showing every frame, and a
    /// frame replaced before it was taken is counted as a drop so the UI can show it.
    ///
    /// Unlike the one camera case there is no per slot wait handle. With up to 48 panes the
    /// render thread cannot wait on 48 events (and WaitAny tops out at 64 anyway), so every
    /// slot on the wall signals one shared event and the render thread drains all of them when
    /// it wakes. The render thread resets that event before it drains, so a push that lands
    /// during the drain still wakes the next iteration rather than being lost.
    /// </summary>
    internal unsafe sealed class FrameSlot : IDisposable
    {
        private readonly object _gate = new object();
        private readonly PaneStats _stats;
        private readonly ManualResetEventSlim _wallWake;

        private DecodedFrame _pending;

        internal FrameSlot(PaneStats stats, ManualResetEventSlim wallWake)
        {
            _stats = stats;
            _wallWake = wallWake;
        }

        internal void Push(DecodedFrame frame)
        {
            DecodedFrame displaced;
            lock (_gate)
            {
                displaced = _pending;
                _pending = frame;
            }

            // Free outside the lock: av_frame_free can return surfaces to the decoder pool.
            if (displaced.Frame != null)
            {
                var p = displaced.Frame;
                ffmpeg.av_frame_free(&p);
                _stats.AddDroppedFrame();
            }

            _wallWake?.Set();
        }

        internal bool TryTake(out DecodedFrame frame)
        {
            lock (_gate)
            {
                frame = _pending;
                _pending = default(DecodedFrame);
            }
            return frame.Frame != null;
        }

        internal void Clear()
        {
            if (TryTake(out var frame))
            {
                var p = frame.Frame;
                ffmpeg.av_frame_free(&p);
            }
        }

        public void Dispose() => Clear();
    }
}
