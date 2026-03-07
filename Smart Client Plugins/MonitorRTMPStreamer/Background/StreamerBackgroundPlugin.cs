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
        private MonitorCapture _capture;
        private string _activeRtmpUrl;
        private DateTime? _reconnectUntil;
        private string _lastScreenKey;
        private StreamerConfig _config;

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
            _config = StreamerConfig.Load();

            while (_run)
            {
                Stopwatch sw = null;
                try
                {
                    var status = StreamerStatus.Instance;

                    // Handle restart requests
                    if (status.RestartRequested)
                    {
                        status.RestartRequested = false;
                        StopStreamer();
                        DisposeCapture();
                        status.ClearError();
                        _reconnectUntil = null;
                        _lastScreenKey = null;
                        _config = StreamerConfig.Load();
                        Log("Stream restart requested.");
                        status.RestartCompleted = true;
                    }

                    // Skip capture entirely when no RTMP URL is configured
                    if (string.IsNullOrWhiteSpace(_config.RtmpUrl))
                    {
                        status.UpdateStreaming(false, "");
                        Thread.Sleep(2000);
                        continue;
                    }

                    var screens = Screen.AllScreens
                        .Where(s => _config.IsMonitorEnabled(s))
                        .ToArray();

                    if (screens.Length == 0)
                    {
                        Thread.Sleep(2000);
                        continue;
                    }

                    // Init or reinit capture when screens change
                    var screenKey = string.Join("|", screens.Select(s => s.DeviceName + s.Bounds));
                    if (_capture == null || _lastScreenKey != screenKey)
                    {
                        DisposeCapture();
                        _capture = new MonitorCapture();
                        _capture.Init(screens, Log);
                        _lastScreenKey = screenKey;
                        Log($"Capture initialised: {_capture.CaptureMethod} for {screens.Length} monitor(s)");
                    }

                    sw = Stopwatch.StartNew();

                    // Capture() returns a reusable bitmap — do NOT dispose it
                    var stitched = _capture.Capture();

                    var captureMs = sw.ElapsedMilliseconds;
                    var encodeStart = sw.ElapsedMilliseconds;

                    status.UpdateCaptureMethod(_capture.CaptureMethod);

                    // Start or reconnect streamer
                    if (_streamer == null)
                    {
                        if (_reconnectUntil.HasValue && DateTime.UtcNow < _reconnectUntil.Value)
                        {
                            var remaining = (int)(_reconnectUntil.Value - DateTime.UtcNow).TotalSeconds + 1;
                            status.SetError($"Reconnecting in {remaining}s...");
                        }
                        else
                        {
                            _reconnectUntil = null;
                            _activeRtmpUrl = _config.RtmpUrl;
                            _streamer = new RtmpStreamer(
                                stitched.Width, stitched.Height,
                                fps: _config.Fps,
                                rtmpUrl: _config.RtmpUrl,
                                log: Log, logError: OnStreamError);

                            if (_streamer.Start())
                            {
                                status.ClearError();
                                Log($"RTMP connected: {_config.RtmpUrl} ({stitched.Width}x{stitched.Height} @ {_config.Fps} FPS, capture: {_capture.CaptureMethod})");
                            }
                            else
                            {
                                StopStreamer();
                                _reconnectUntil = DateTime.UtcNow.AddSeconds(5);
                            }
                        }
                    }

                    // Push frame
                    if (_streamer?.IsRunning == true)
                    {
                        _streamer.PushFrame(stitched);

                        if (!_streamer.IsRunning)
                        {
                            Log("RTMP connection lost. Will reconnect.");
                            StopStreamer();
                            _reconnectUntil = DateTime.UtcNow.AddSeconds(5);
                        }
                    }

                    var encodeMs = sw.ElapsedMilliseconds - encodeStart;
                    var totalMs = sw.ElapsedMilliseconds;

                    status.UpdateCycle(screens.Length, stitched.Width, stitched.Height, captureMs, encodeMs, totalMs);
                    status.UpdateStreaming(_streamer?.IsRunning == true, _activeRtmpUrl ?? _config.RtmpUrl);
                }
                catch (Exception e)
                {
                    StreamerStatus.Instance.SetError(e.Message);
                    LogError($"Cycle error: {e.Message}");
                }

                // Sleep only the remaining time to hit target frame interval
                var fps = _config?.Fps ?? 1;
                var frameInterval = 1000 / fps;
                var elapsed = (int)(sw?.ElapsedMilliseconds ?? 0);
                var sleepMs = Math.Max(1, frameInterval - elapsed);
                Thread.Sleep(sleepMs);
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

        private void DisposeCapture()
        {
            _capture?.Dispose();
            _capture = null;
        }

        public override void Close()
        {
            _run = false;
            _captureThread?.Join(3000);
            StopStreamer();
            DisposeCapture();
            StreamerStatus.Instance.UpdateStreaming(false, "");
            Log("MonitorRTMPStreamer stopped.");
        }

        private void Log(string msg)
            => EnvironmentManager.Instance.Log(false, nameof(StreamerBackgroundPlugin), msg);

        private void LogError(string msg)
            => EnvironmentManager.Instance.Log(true, nameof(StreamerBackgroundPlugin), msg);
    }
}
