using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
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

        private readonly object _lock = new object();
        private List<WindowInfo> _windows = new List<WindowInfo>();
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
                    var windows = ProcessWindows.GetAllWindowsForCurrentProcess();

                    lock (_lock)
                        _windows = windows;

                    Log($"Cycle: found {windows.Count} window(s)");

                    foreach (var win in windows)
                    {
                        if (!_run) break;

                        if (!win.IsVisible)
                        {
                            Log($"  Skip hidden: {win}");
                            continue;
                        }
                        if (win.IsMinimized || !win.HasArea)
                        {
                            Log($"  Skip minimized/no-area: {win}");
                            continue;
                        }

                        try
                        {
                            using (var bmp = Capture.CaptureWindow(win.Handle))
                            {
                                var safeName = SanitizeFileName(win.Title);
                                if (string.IsNullOrEmpty(safeName))
                                    safeName = win.Handle.ToString();

                                var path = Path.Combine(_outputDir, $"{safeName}.png");

                                using (var ms = new MemoryStream())
                                {
                                    bmp.Save(ms, ImageFormat.Png);
                                    File.WriteAllBytes(path, ms.ToArray());
                                }
                                Log($"  Snap {win.Width}x{win.Height} \"{win.Title}\" -> {path}");
                            }
                        }
                        catch (Exception e)
                        {
                            LogError($"  Capture failed for {win}: {e.Message}");
                        }
                    }
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
            if (string.IsNullOrWhiteSpace(name)) return null;
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
