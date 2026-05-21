using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace LiveExporter.Client
{
    /// <summary>
    /// Mirrors the most recently clicked camera in an ImageViewerWpfControl and runs it in
    /// independent playback (own PlaybackController + scrubber). The operator scrubs to the
    /// desired start, clicks "Set start", scrubs to the end, clicks "Set end", then "Add to
    /// Export" - which posts the (camera, start..end) pair to the Smart Client's built-in
    /// export list. Camera follows HotspotCameraSource so tile clicks, Map clicks, Smart Map
    /// clicks etc. update the flyout live.
    /// </summary>
    public partial class LiveExporterFlyoutWindow : Window
    {
        private ImageViewerWpfControl _viewer;
        private PlaybackWpfUserControl _scrubber;
        private FQID _playbackControllerFqid;
        private FQID _currentCameraFqid;

        private DateTime? _startUtc;
        private DateTime? _endUtc;

        public LiveExporterFlyoutWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += OnKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsurePlaybackController();
                BuildViewer();
                BuildScrubber();
                HotspotCameraSource.CameraChanged += OnHotspotCameraChanged;

                // Pre-populate with whatever camera the operator most recently clicked.
                var current = HotspotCameraSource.GetCurrentCameraFqid();
                if (current != null) ApplyCamera(current);
                else SetStatus("Click any camera in Smart Client to load it.");

                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                LiveExporterDefinition.Log.Error("LiveExporterFlyout: Loaded failed", ex);
                SetStatus("Failed to initialise: " + ex.Message, error: true);
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try { HotspotCameraSource.CameraChanged -= OnHotspotCameraChanged; } catch { }
            TeardownScrubber();
            TeardownViewer();
            ReleasePlaybackController();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }

        // --- Hotspot subscription ---

        private void OnHotspotCameraChanged(object sender, EventArgs e)
        {
            try { Dispatcher.BeginInvoke(new Action(() =>
            {
                var fqid = HotspotCameraSource.GetCurrentCameraFqid();
                if (fqid != null) ApplyCamera(fqid);
            })); }
            catch { }
        }

        // --- Wiring ---

        private void BuildViewer()
        {
            _viewer = new ImageViewerWpfControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                EnableMouseControlledPtz = true,
                EnableDigitalZoom = true,
                EnableVisibleHeader = true,
                EnableVisibleCameraName = true,
                EnableVisibleLiveIndicator = true,
                EnableVisibleTimestamp = true,
                MaintainImageAspectRatio = true,
            };
            videoHost.Children.Add(_viewer);
        }

        private void TeardownViewer()
        {
            if (_viewer == null) return;
            try { _viewer.Disconnect(); } catch { }
            try { _viewer.Close(); } catch { }
            _viewer = null;
        }

        private void EnsurePlaybackController()
        {
            if (_playbackControllerFqid != null) return;
            try
            {
                _playbackControllerFqid = ClientControl.Instance.GeneratePlaybackController();
                var pc = ClientControl.Instance.GetPlaybackController(_playbackControllerFqid);
                if (pc != null) pc.SkipGaps = false;
            }
            catch (Exception ex)
            {
                LiveExporterDefinition.Log.Error("Failed to generate PlaybackController", ex);
            }
        }

        private void ReleasePlaybackController()
        {
            if (_playbackControllerFqid == null) return;
            try { ClientControl.Instance.ReleasePlaybackController(_playbackControllerFqid); }
            catch (Exception ex) { LiveExporterDefinition.Log.Error("ReleasePlaybackController failed", ex); }
            _playbackControllerFqid = null;
        }

        private void BuildScrubber()
        {
            if (_scrubber != null || _playbackControllerFqid == null) return;
            try
            {
                _scrubber = new PlaybackWpfUserControl();
                scrubberHost.Children.Add(_scrubber);
                _scrubber.Init(_playbackControllerFqid);
            }
            catch (Exception ex)
            {
                LiveExporterDefinition.Log.Error("Failed to build playback scrubber", ex);
            }
        }

        private void TeardownScrubber()
        {
            if (_scrubber == null) return;
            try { _scrubber.Close(); } catch { }
            try { scrubberHost.Children.Remove(_scrubber); } catch { }
            _scrubber = null;
        }

        // --- Camera swap ---

        private void ApplyCamera(FQID cameraFqid)
        {
            if (cameraFqid == null || _viewer == null) return;
            if (_currentCameraFqid != null && _currentCameraFqid.ObjectId == cameraFqid.ObjectId)
                return;

            try
            {
                try { _viewer.Disconnect(); } catch { }

                _viewer.CameraFQID = cameraFqid;
                _viewer.PlaybackControllerFQID = _playbackControllerFqid;
                _viewer.Initialize();
                _viewer.Connect();
                _viewer.StartBrowse();

                _currentCameraFqid = cameraFqid;

                // Switching camera invalidates any previous start/end - they were anchored to
                // the prior camera's timeline; carrying them across feels like a footgun.
                _startUtc = null;
                _endUtc = null;

                cameraNameText.Text = ResolveCameraName(cameraFqid) ?? "(camera)";
                RefreshTimeLabels();
                UpdateButtonStates();
                SetStatus(string.Empty);
            }
            catch (Exception ex)
            {
                LiveExporterDefinition.Log.Error($"ApplyCamera failed: {cameraFqid}", ex);
                SetStatus("Failed to load camera: " + ex.Message, error: true);
            }
        }

        private static string ResolveCameraName(FQID fqid)
        {
            try { return Configuration.Instance.GetItem(fqid)?.Name; }
            catch { return null; }
        }

        // --- Chip button handlers ---

        private void OnSetStartClick(object sender, RoutedEventArgs e)
        {
            var t = ReadPlayheadUtc();
            if (t == null) { SetStatus("Playback time not available yet.", error: true); return; }
            _startUtc = t;
            RefreshTimeLabels();
            UpdateButtonStates();
            SetStatus(string.Empty);
        }

        private void OnSetEndClick(object sender, RoutedEventArgs e)
        {
            var t = ReadPlayheadUtc();
            if (t == null) { SetStatus("Playback time not available yet.", error: true); return; }
            _endUtc = t;
            RefreshTimeLabels();
            UpdateButtonStates();
            SetStatus(string.Empty);
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            if (_currentCameraFqid == null) { SetStatus("Pick a camera first.", error: true); return; }
            if (_startUtc == null || _endUtc == null) { SetStatus("Set both start and end.", error: true); return; }

            if (ExportListSender.Send(_currentCameraFqid, _startUtc.Value, _endUtc.Value, out var err))
            {
                SetStatus("Added to export list.", error: false);
                // Auto-reset so the operator can mark another range without an extra click.
                _startUtc = null;
                _endUtc = null;
                RefreshTimeLabels();
                UpdateButtonStates();
            }
            else
            {
                SetStatus("Add failed: " + (err ?? "unknown"), error: true);
            }
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _startUtc = null;
            _endUtc = null;
            RefreshTimeLabels();
            UpdateButtonStates();
            SetStatus(string.Empty);
        }

        // --- Helpers ---

        private DateTime? ReadPlayheadUtc()
        {
            if (_playbackControllerFqid == null) return null;
            try
            {
                var pc = ClientControl.Instance.GetPlaybackController(_playbackControllerFqid);
                return pc?.PlaybackTime;
            }
            catch (Exception ex)
            {
                LiveExporterDefinition.Log.Error("ReadPlayheadUtc failed", ex);
                return null;
            }
        }

        private void RefreshTimeLabels()
        {
            startTimeText.Text = _startUtc.HasValue ? FormatLocal(_startUtc.Value) : "(not set)";
            endTimeText.Text   = _endUtc.HasValue   ? FormatLocal(_endUtc.Value)   : "(not set)";
        }

        private static string FormatLocal(DateTime utc)
        {
            try { return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { return utc.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"; }
        }

        private void UpdateButtonStates()
        {
            var hasCamera = _currentCameraFqid != null;
            setStartButton.IsEnabled = hasCamera;
            setEndButton.IsEnabled = hasCamera;
            resetButton.IsEnabled = _startUtc.HasValue || _endUtc.HasValue;
            addButton.IsEnabled = hasCamera
                && _startUtc.HasValue && _endUtc.HasValue
                && _endUtc.Value > _startUtc.Value;
        }

        private void SetStatus(string text, bool error = false)
        {
            statusText.Text = text ?? string.Empty;
            statusText.Foreground = error
                ? (Brush)new BrushConverter().ConvertFromString("#FFE05A4F")
                : (Brush)new BrushConverter().ConvertFromString("#FF8C8C8C");
        }
    }
}
