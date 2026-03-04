using System;
using System.Threading;
using VideoOS.Platform;
using VideoOS.Platform.Live;

namespace SnapReport.Services
{
    internal static class SnapshotService
    {
        /// <summary>
        /// Grabs a single JPEG snapshot from a camera.
        /// Pattern based on Milestone's official MilestonePSTools GetSnapshot implementation.
        /// </summary>
        public static (byte[] ImageData, DateTime Timestamp, string Error) GrabSnapshot(Item cameraItem, int timeoutMs = 5000)
        {
            byte[] result = null;
            DateTime timestamp = DateTime.MinValue;
            string error = null;
            var signal = new ManualResetEventSlim(false);
            var contentLock = new object();
            var disposed = false;
            JPEGLiveSource src = null;

            try
            {
                src = new JPEGLiveSource(cameraItem)
                {
                    SendInitialImage = false
                };

                src.LiveContentEvent += (sender, e) =>
                {
                    try
                    {
                        lock (contentLock)
                        {
                            if (disposed) return;
                            if (signal.IsSet) return;
                            if (!(e is LiveContentEventArgs liveContent)) return;
                            if (liveContent.Exception != null) return;

                            var content = liveContent.LiveContent;
                            if (content?.Content is byte[] data && data.Length > 0)
                            {
                                result = data;
                                timestamp = content.EndTime;
                                signal.Set();
                            }
                        }
                    }
                    catch
                    {
                        // Never crash the poll thread
                    }
                };

                src.LiveStatusEvent += (sender, e) =>
                {
                    // Swallow status events — we only care about content
                };

                src.Init();
                src.LiveModeStart = true;

                if (!signal.Wait(timeoutMs))
                    error = "Snapshot timed out";
            }
            catch (CommunicationMIPException)
            {
                error = $"Unable to connect to {cameraItem.Name}";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                lock (contentLock)
                {
                    disposed = true;
                }

                if (src != null)
                {
                    try { src.LiveModeStart = false; } catch { }
                    try { src.Close(); } catch { }
                }

                try { signal.Dispose(); } catch { }
            }

            return (result, timestamp, error);
        }
    }
}
