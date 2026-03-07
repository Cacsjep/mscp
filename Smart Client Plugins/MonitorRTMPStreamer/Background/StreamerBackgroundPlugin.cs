using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Background;

namespace MonitorRTMPStreamer.Background
{
    public class StreamerBackgroundPlugin : BackgroundPlugin
    {
        public override Guid Id => StreamerDefinition.StreamerBackgroundPluginId;

        public override string Name => "MonitorRTMPStreamer BackgroundPlugin";

        public override List<EnvironmentType> TargetEnvironments
            => new List<EnvironmentType> { EnvironmentType.SmartClient };

        private Thread _captureThread;
        private volatile bool _run;
        private RtmpStreamer _streamer;
        private string _activeRtmpUrl;
        private int _reconnectDelay;

        public override void Init()
        {
            _run = true;
            _captureThread = new Thread(CaptureLoop) { IsBackground = true };
            _captureThread.Start();
            Log("MonitorRTMPStreamer started.");
        }

        private void CaptureLoop()
        {
            Thread.Sleep(5000);

            while (_run)
            {
                try
                {
                    var config = StreamerConfig.Load();
                    var status = StreamerStatus.Instance;

                    // Handle restart requests (URL changed from settings panel)
                    if (status.RestartRequested)
                    {
                        status.RestartRequested = false;
                        StopStreamer();
                        status.ClearError();
                        _reconnectDelay = 0;
                        Log("Stream restart requested.");
                        status.RestartCompleted = true;
                    }

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
                        var encodeStart = sw.ElapsedMilliseconds;

                        // Start or reconnect streamer
                        if (_streamer == null && !string.IsNullOrWhiteSpace(config.RtmpUrl))
                        {
                            if (_reconnectDelay > 0)
                            {
                                _reconnectDelay--;
                                status.SetError($"Reconnecting in {_reconnectDelay + 1}s...");
                            }
                            else
                            {
                                _activeRtmpUrl = config.RtmpUrl;
                                _streamer = new RtmpStreamer(
                                    stitched.Width, stitched.Height,
                                    rtmpUrl: config.RtmpUrl,
                                    log: Log, logError: OnStreamError);

                                if (_streamer.Start())
                                {
                                    status.ClearError();
                                    _reconnectDelay = 0;
                                    Log($"RTMP connected: {config.RtmpUrl} ({stitched.Width}x{stitched.Height})");
                                }
                                else
                                {
                                    StopStreamer();
                                    _reconnectDelay = 5; // retry in 5 seconds
                                }
                            }
                        }

                        // Push frame - if it fails, dispose and let reconnect kick in
                        if (_streamer?.IsRunning == true)
                        {
                            _streamer.PushFrame(stitched);

                            if (!_streamer.IsRunning)
                            {
                                Log("RTMP connection lost. Will reconnect.");
                                StopStreamer();
                                _reconnectDelay = 5;
                            }
                        }

                        var encodeMs = sw.ElapsedMilliseconds - encodeStart;
                        var totalMs = sw.ElapsedMilliseconds;

                        status.UpdateCycle(screens.Length, stitched.Width, stitched.Height, captureMs, encodeMs, totalMs);
                        status.UpdateStreaming(_streamer?.IsRunning == true, _activeRtmpUrl ?? config.RtmpUrl);
                    }
                }
                catch (Exception e)
                {
                    StreamerStatus.Instance.SetError(e.Message);
                    LogError($"Cycle error: {e.Message}");
                }

                Thread.Sleep(1000);
            }
        }

        private void OnStreamError(string msg)
        {
            StreamerStatus.Instance.SetError(msg);
            LogError(msg);
        }

        private void StopStreamer()
        {
            _streamer?.Dispose();
            _streamer = null;
            _activeRtmpUrl = null;
        }

        public override void Close()
        {
            _run = false;
            _captureThread?.Join(3000);
            StopStreamer();
            StreamerStatus.Instance.UpdateStreaming(false, "");
            Log("MonitorRTMPStreamer stopped.");
        }

        private void Log(string msg)
            => EnvironmentManager.Instance.Log(false, nameof(StreamerBackgroundPlugin), msg);

        private void LogError(string msg)
            => EnvironmentManager.Instance.Log(true, nameof(StreamerBackgroundPlugin), msg);
    }
}
