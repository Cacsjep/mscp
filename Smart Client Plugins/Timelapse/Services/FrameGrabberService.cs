using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace Timelapse.Services
{
    /// <summary>
    /// Reusable frame source for a single camera. Keeps the JPEGVideoSource alive
    /// to allow sequential frame retrieval without re-initialization overhead.
    /// Each instance is NOT thread-safe - use one per worker thread.
    /// </summary>
    internal sealed class CameraFrameSource : IDisposable
    {
        private JPEGVideoSource _source;
        private bool _disposed;

        public Item CameraItem { get; }

        public CameraFrameSource(Item cameraItem)
        {
            CameraItem = cameraItem;
            _source = new JPEGVideoSource(cameraItem);
            _source.Init();
        }

        /// <summary>
        /// Fetches the frame closest to the requested time.
        /// </summary>
        public (Bitmap Frame, string Error) GetFrame(DateTime requestedTime)
        {
            if (_disposed) return (null, "Source disposed");

            try
            {
                // GoTo seeks the source position, then we get the nearest frame
                _source.GoTo(requestedTime, "");

                var data = _source.GetNearest(requestedTime) as JPEGData;
                if (data == null)
                    data = _source.GetAtOrBefore(requestedTime) as JPEGData;
                if (data == null)
                    data = _source.Get(requestedTime) as JPEGData;

                if (data == null)
                    return (null, $"No frame at {requestedTime:HH:mm:ss}");

                var bitmapSource = data.ConvertToBitmapSource();
                if (bitmapSource == null)
                    return (null, "Decode failed");

                return (BitmapSourceToGdiBitmap(bitmapSource), null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>
        /// Does this camera have any recording inside [from, to]?
        /// Delegates to SequenceDataSource (RecordingSequence) so we get a range-aware answer.
        /// </summary>
        public bool HasRecordings(DateTime from, DateTime to)
        {
            try
            {
                using (var q = new SequenceQuery(CameraItem))
                    return q.AnyRecording(from, to);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _source?.Close(); } catch { }
            _source = null;
        }

        private static Bitmap BitmapSourceToGdiBitmap(BitmapSource bitmapSource)
        {
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }
        }
    }

    internal static class FrameGrabberService
    {
        /// <summary>
        /// Generates a list of timestamps between start and end at the given interval.
        /// </summary>
        public static List<DateTime> GenerateTimestamps(DateTime start, DateTime end, TimeSpan interval)
        {
            var timestamps = new List<DateTime>();
            for (var t = start; t <= end; t += interval)
            {
                timestamps.Add(t);
            }
            return timestamps;
        }
    }
}
