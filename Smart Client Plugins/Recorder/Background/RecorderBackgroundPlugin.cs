using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace Recorder.Background
{
    public class RecorderBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => RecorderDefinition.RecorderBackgroundPluginId;

        public override string Name => "Recorder BackgroundPlugin";

        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        private Thread _captureThread;
        private volatile bool _run;
        private string _outputDir;
        private RtmpStreamer _streamer;

        public override void Init()
        {
            _outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "RecorderCaptures");
            Directory.CreateDirectory(_outputDir);

            _run = true;
            _captureThread = new Thread(CaptureLoop) { IsBackground = true };
            _captureThread.Start();
            Log($"Recorder started. Output: {_outputDir}");
        }

        private void CaptureLoop()
        {
            Thread.Sleep(5000);

            while (_run)
            {
                try
                {
                    var config = RecorderConfig.Load();
                    var screens = Screen.AllScreens
                        .Where(s => config.IsMonitorEnabled(s))
                        .ToArray();

                    if (screens.Length == 0)
                    {
                        Thread.Sleep(2000);
                        continue;
                    }

                    var sw = Stopwatch.StartNew();

                    using (var stitched = MonitorCapture.CaptureAndStitch(screens))
                    {
                        var captureMs = sw.ElapsedMilliseconds;

                        // Start RTMP streamer on first frame (we now know the resolution)
                        if (_streamer == null && !string.IsNullOrWhiteSpace(config.RtmpUrl))
                        {
                            _streamer = new RtmpStreamer(
                                stitched.Width, stitched.Height,
                                rtmpUrl: config.RtmpUrl,
                                log: Log, logError: LogError);
                            _streamer.Start();
                        }

                        // Push frame to RTMP stream
                        if (_streamer?.IsRunning == true)
                            _streamer.PushFrame(stitched);

                        // Also save snapshot to disk
                        var path = Path.Combine(_outputDir, "stitched.png");
                        using (var ms = new MemoryStream())
                        {
                            stitched.Save(ms, ImageFormat.Png);
                            File.WriteAllBytes(path, ms.ToArray());
                        }

                        var totalMs = sw.ElapsedMilliseconds;
                        var sizeKb = new FileInfo(path).Length / 1024;
                        Log($"Cycle: {screens.Length} monitor(s) stitched={stitched.Width}x{stitched.Height} "
                          + $"capture={captureMs}ms total={totalMs}ms size={sizeKb}KB "
                          + $"rtmp={(_streamer?.IsRunning == true ? "streaming" : "off")}");
                    }
                }
                catch (Exception e)
                {
                    LogError($"Cycle error: {e}");
                }

                Thread.Sleep(1000);
            }
        }

        public override void Close()
        {
            _run = false;
            _captureThread?.Join(3000);
            _streamer?.Dispose();
            _streamer = null;
            Log("Recorder stopped.");
        }

        private void Log(string msg)
            => EnvironmentManager.Instance.Log(false, nameof(RecorderBackgroundPlugin), msg);

        private void LogError(string msg)
            => EnvironmentManager.Instance.Log(true, nameof(RecorderBackgroundPlugin), msg);
    }
}
