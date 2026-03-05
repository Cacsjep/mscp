using System;
using System.Collections.Generic;
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

        Thread caputreTread;
        IntPtr mainWindow;
        bool run = true;

        public override void Init()
        {
            EnvironmentManager.Instance.Log(false, nameof(RecorderBackgroundPlugin), "Recorder plugin started.");
            var w = ProcessWindows.GetMainTopLevelWindow();
            if (w == null)
            {
                EnvironmentManager.Instance.Log(true, nameof(RecorderBackgroundPlugin), "Unable to find main window handle.");
                return;
            }
            mainWindow = w;
            caputreTread = new Thread(new ThreadStart(CaptureLoop));
            caputreTread.Start();
        }

        void CaptureLoop()
        {
            while (run)
            {
                try
                {
                    var bmp = Capture.CaptureWindow(mainWindow);
                    bmp.Save("capture.png");
                    EnvironmentManager.Instance.Log(false, nameof(RecorderBackgroundPlugin), "Snap Created.");
                }
                catch (Exception e)
                {
                    EnvironmentManager.Instance.Log(true, nameof(RecorderBackgroundPlugin), e.Message);
                }
                Thread.Sleep(2000);
            }
        }

        public override void Close()
        {
            run = false;
            if (caputreTread != null)
            {
                caputreTread.Abort();
            }
            EnvironmentManager.Instance.Log(false, nameof(RecorderBackgroundPlugin), "Recorder plugin stopped.");
        }
    }
}
