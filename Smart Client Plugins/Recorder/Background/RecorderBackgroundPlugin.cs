using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Recorder.Background;
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
                        .ToList();

                    var sw = Stopwatch.StartNew();
                    var captured = 0;

                    foreach (var screen in screens)
                    {
                        if (!_run) break;

                        try
                        {
                            var snapSw = Stopwatch.StartNew();
                            using (var bmp = MonitorCapture.CaptureScreen(screen))
                            {
                                var captureMs = snapSw.ElapsedMilliseconds;

                                var safeName = SanitizeFileName(screen.DeviceName);
                                var path = Path.Combine(_outputDir, $"{safeName}.png");

                                using (var ms = new MemoryStream())
                                {
                                    bmp.Save(ms, ImageFormat.Png);
                                    File.WriteAllBytes(path, ms.ToArray());
                                }
                                var totalMs = snapSw.ElapsedMilliseconds;
                                var sizeKb = new FileInfo(path).Length / 1024;
                                Log($"  Snap {screen.DeviceName} {screen.Bounds.Width}x{screen.Bounds.Height} capture={captureMs}ms encode+save={totalMs - captureMs}ms total={totalMs}ms size={sizeKb}KB");
                                captured++;
                            }
                        }
                        catch (Exception e)
                        {
                            LogError($"  Capture failed for {screen.DeviceName}: {e.Message}");
                        }
                    }

                    Log($"Cycle: {captured}/{screens.Count} monitor(s) in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception e)
                {
                    LogError($"Cycle error: {e}");
                }

                Thread.Sleep(2000);
            }
        }

        public override void Close()
        {
            _run = false;
            _captureThread?.Join(3000);
            Log("Recorder stopped.");
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unknown";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private void Log(string msg)
            => EnvironmentManager.Instance.Log(false, nameof(RecorderBackgroundPlugin), msg);

        private void LogError(string msg)
            => EnvironmentManager.Instance.Log(true, nameof(RecorderBackgroundPlugin), msg);
    }
}
