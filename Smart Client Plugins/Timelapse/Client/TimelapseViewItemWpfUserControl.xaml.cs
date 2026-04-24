using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunitySDK;
using Microsoft.Win32;
using Timelapse.Services;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI;

namespace Timelapse.Client
{
    public partial class TimelapseViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private static readonly PluginLog Log = new PluginLog("Timelapse");

        private readonly ObservableCollection<CameraEntry> _cameras = new ObservableCollection<CameraEntry>();
        private CancellationTokenSource _cts;
        private string _currentVideoPath;
        private DispatcherTimer _playbackTimer;
        private bool _isSeeking;
        private bool _wasPlayingBeforeSeek;

        // Preflight (sequence list per camera) - debounced
        private DispatcherTimer _preflightDebounce;
        private CancellationTokenSource _preflightCts;
        private IReadOnlyDictionary<Guid, IReadOnlyList<RecordingSegment>> _preflightSegments
            = new Dictionary<Guid, IReadOnlyList<RecordingSegment>>();

        public TimelapseViewItemWpfUserControl()
        {
            InitializeComponent();
            cameraListBox.ItemsSource = _cameras;
            InitializeControls();
        }

        public override bool ShowToolbar => false;
        public override bool Selectable => false;

        public override void Init() { }
        public override void Close()
        {
            _cts?.Cancel();
            _playbackTimer?.Stop();
            mediaPlayer.Stop();
            mediaPlayer.Close();
            CleanupTempVideo();
        }

        #region Initialization

        private void InitializeControls()
        {
            // Hours 00-23
            for (int i = 0; i < 24; i++)
            {
                var h = i.ToString("D2");
                startHourCombo.Items.Add(h);
                endHourCombo.Items.Add(h);
            }
            // Minutes 00, 15, 30, 45
            foreach (var m in new[] { "00", "15", "30", "45" })
            {
                startMinuteCombo.Items.Add(m);
                endMinuteCombo.Items.Add(m);
            }

            startHourCombo.SelectedIndex = 8;  // 08:00
            startMinuteCombo.SelectedIndex = 0;
            endHourCombo.SelectedIndex = 17;   // 17:00
            endMinuteCombo.SelectedIndex = 0;

            startDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
            endDatePicker.SelectedDate = DateTime.Today.AddDays(-1);

            // Time presets
            timePresetCombo.Items.Add(new TimePresetOption("Custom", TimeSpan.Zero));
            timePresetCombo.Items.Add(new TimePresetOption("Last 4 Hours", TimeSpan.FromHours(4)));
            timePresetCombo.Items.Add(new TimePresetOption("Last 8 Hours", TimeSpan.FromHours(8)));
            timePresetCombo.Items.Add(new TimePresetOption("Last 24 Hours", TimeSpan.FromHours(24)));
            timePresetCombo.Items.Add(new TimePresetOption("Last 2 Days", TimeSpan.FromDays(2)));
            timePresetCombo.Items.Add(new TimePresetOption("Last 4 Days", TimeSpan.FromDays(4)));
            timePresetCombo.Items.Add(new TimePresetOption("Last 6 Days", TimeSpan.FromDays(6)));
            timePresetCombo.Items.Add(new TimePresetOption("Last Week", TimeSpan.FromDays(7)));
            timePresetCombo.Items.Add(new TimePresetOption("Last 2 Weeks", TimeSpan.FromDays(14)));
            timePresetCombo.Items.Add(new TimePresetOption("Last 3 Weeks", TimeSpan.FromDays(21)));
            timePresetCombo.Items.Add(new TimePresetOption("Last 4 Weeks", TimeSpan.FromDays(28)));
            timePresetCombo.SelectedIndex = 0;
            timePresetCombo.SelectionChanged += OnTimePresetChanged;

            // Batch size
            foreach (var b in new[] { 10, 20, 50, 100, 200 })
                batchSizeCombo.Items.Add(new BatchSizeOption(b));
            batchSizeCombo.SelectedIndex = 2; // 50

            // Mode
            modeCombo.Items.Add(new ModeOption("Continuous", TimelapseMode.Continuous));
            modeCombo.Items.Add(new ModeOption("Event-based", TimelapseMode.EventBased));
            modeCombo.SelectedIndex = 0;
            modeCombo.SelectionChanged += OnModeChanged;

            // Frame interval options (continuous)
            intervalCombo.Items.Add(new IntervalOption("Every 10 seconds", TimeSpan.FromSeconds(10)));
            intervalCombo.Items.Add(new IntervalOption("Every 30 seconds", TimeSpan.FromSeconds(30)));
            intervalCombo.Items.Add(new IntervalOption("Every 1 minute", TimeSpan.FromMinutes(1)));
            intervalCombo.Items.Add(new IntervalOption("Every 5 minutes", TimeSpan.FromMinutes(5)));
            intervalCombo.Items.Add(new IntervalOption("Every 10 minutes", TimeSpan.FromMinutes(10)));
            intervalCombo.Items.Add(new IntervalOption("Every 15 minutes", TimeSpan.FromMinutes(15)));
            intervalCombo.Items.Add(new IntervalOption("Every 30 minutes", TimeSpan.FromMinutes(30)));
            intervalCombo.Items.Add(new IntervalOption("Every 1 hour", TimeSpan.FromHours(1)));
            intervalCombo.SelectedIndex = 2; // 1 minute

            // Event-based interval (shorter options)
            eventIntervalCombo.Items.Add(new IntervalOption("Every 1 second", TimeSpan.FromSeconds(1)));
            eventIntervalCombo.Items.Add(new IntervalOption("Every 2 seconds", TimeSpan.FromSeconds(2)));
            eventIntervalCombo.Items.Add(new IntervalOption("Every 5 seconds", TimeSpan.FromSeconds(5)));
            eventIntervalCombo.Items.Add(new IntervalOption("Every 10 seconds", TimeSpan.FromSeconds(10)));
            eventIntervalCombo.Items.Add(new IntervalOption("Every 30 seconds", TimeSpan.FromSeconds(30)));
            eventIntervalCombo.Items.Add(new IntervalOption("Every 1 minute", TimeSpan.FromMinutes(1)));
            eventIntervalCombo.SelectedIndex = 3; // 10s

            // Max frames per event
            foreach (var n in new[] { 1, 3, 5, 10, 20, 50, 100 })
                maxPerEventCombo.Items.Add(new CountOption(n));
            maxPerEventCombo.SelectedIndex = 3; // 10

            // Min frames per event
            foreach (var n in new[] { 1, 2, 3, 5 })
                minPerEventCombo.Items.Add(new CountOption(n));
            minPerEventCombo.SelectedIndex = 0; // 1

            // Event merge gap (Advanced)
            mergeGapCombo.Items.Add(new GapOption("None", TimeSpan.Zero));
            mergeGapCombo.Items.Add(new GapOption("1 s", TimeSpan.FromSeconds(1)));
            mergeGapCombo.Items.Add(new GapOption("2 s", TimeSpan.FromSeconds(2)));
            mergeGapCombo.Items.Add(new GapOption("5 s", TimeSpan.FromSeconds(5)));
            mergeGapCombo.Items.Add(new GapOption("10 s", TimeSpan.FromSeconds(10)));
            mergeGapCombo.SelectedIndex = 2; // 2 s

            // Output FPS
            foreach (var fps in new[] { 5, 10, 15, 24, 30 })
                fpsCombo.Items.Add(new FpsOption(fps));
            fpsCombo.SelectedIndex = 3; // 24 fps

            // Resolution
            resolutionCombo.Items.Add(new ResolutionOption("Original", 1.0));
            resolutionCombo.Items.Add(new ResolutionOption("Half (50%)", 0.5));
            resolutionCombo.Items.Add(new ResolutionOption("Quarter (25%)", 0.25));
            resolutionCombo.SelectedIndex = 0;

            // Max workers
            foreach (var w in new[] { 1, 2, 3, 5, 8, 10 })
                workersCombo.Items.Add(new WorkersOption(w));
            workersCombo.SelectedIndex = 3; // 5 workers

            // Timestamp overlay settings
            tsPositionCombo.Items.Add("Bottom-Left");
            tsPositionCombo.Items.Add("Bottom-Right");
            tsPositionCombo.Items.Add("Top-Left");
            tsPositionCombo.Items.Add("Top-Right");
            tsPositionCombo.SelectedIndex = 0;

            tsFormatCombo.Items.Add(new TsFormatOption("Date + Time", "yyyy-MM-dd HH:mm:ss"));
            tsFormatCombo.Items.Add(new TsFormatOption("Time only", "HH:mm:ss"));
            tsFormatCombo.Items.Add(new TsFormatOption("Date only", "yyyy-MM-dd"));
            tsFormatCombo.SelectedIndex = 0;

            tsColorCombo.Items.Add(new TsColorOption("White", Color.White));
            tsColorCombo.Items.Add(new TsColorOption("Black", Color.Black));
            tsColorCombo.Items.Add(new TsColorOption("Yellow", Color.Yellow));
            tsColorCombo.Items.Add(new TsColorOption("Red", Color.FromArgb(255, 80, 80)));
            tsColorCombo.SelectedIndex = 0;

            tsBgCombo.Items.Add(new TsBgOption("Dark shadow", Color.FromArgb(160, 0, 0, 0)));
            tsBgCombo.Items.Add(new TsBgOption("Light shadow", Color.FromArgb(160, 255, 255, 255)));
            tsBgCombo.Items.Add(new TsBgOption("None", Color.Empty));
            tsBgCombo.SelectedIndex = 0;

            tsFontSizeCombo.Items.Add(new TsFontSizeOption("Small", 14f));
            tsFontSizeCombo.Items.Add(new TsFontSizeOption("Medium", 22f));
            tsFontSizeCombo.Items.Add(new TsFontSizeOption("Large", 34f));
            tsFontSizeCombo.Items.Add(new TsFontSizeOption("Extra Large", 48f));
            tsFontSizeCombo.SelectedIndex = 1;

            // Playback timer for seek slider
            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _playbackTimer.Tick += OnPlaybackTimerTick;

            seekSlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
                new System.Windows.Controls.Primitives.DragStartedEventHandler((s, ev) =>
                {
                    _isSeeking = true;
                    // Remember if playing, then pause during seek
                    _wasPlayingBeforeSeek = playPauseButton.Content.ToString() == "\u23F8";
                    if (_wasPlayingBeforeSeek)
                    {
                        mediaPlayer.Pause();
                        _playbackTimer.Stop();
                    }
                }));
            seekSlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
                new System.Windows.Controls.Primitives.DragCompletedEventHandler((s, ev) =>
                {
                    _isSeeking = false;
                    // Seek to final position
                    if (mediaPlayer.NaturalDuration.HasTimeSpan)
                    {
                        var duration = mediaPlayer.NaturalDuration.TimeSpan;
                        mediaPlayer.Position = TimeSpan.FromSeconds(duration.TotalSeconds * (seekSlider.Value / 100.0));
                    }
                    // Resume if was playing
                    if (_wasPlayingBeforeSeek)
                    {
                        mediaPlayer.Play();
                        _playbackTimer.Start();
                        playPauseButton.Content = "\u23F8";
                    }
                }));

            // Update estimate when settings change (fast, no server call)
            intervalCombo.SelectionChanged += (s, e) => UpdateEstimate();
            eventIntervalCombo.SelectionChanged += (s, e) => UpdateEstimate();
            maxPerEventCombo.SelectionChanged += (s, e) => UpdateEstimate();
            minPerEventCombo.SelectionChanged += (s, e) => UpdateEstimate();
            mergeGapCombo.SelectionChanged += (s, e) => UpdateEstimate();
            fpsCombo.SelectionChanged += (s, e) => UpdateEstimate();

            // Preflight (debounced, only on cameras/start/end changes)
            _preflightDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _preflightDebounce.Tick += async (s, e) => { _preflightDebounce.Stop(); await RunPreflightAsync(); };

            _cameras.CollectionChanged += (s, e) => SchedulePreflight();
            startDatePicker.SelectedDateChanged += (s, e) => { SchedulePreflight(); UpdateEstimate(); };
            endDatePicker.SelectedDateChanged += (s, e) => { SchedulePreflight(); UpdateEstimate(); };
            startHourCombo.SelectionChanged += (s, e) => { SchedulePreflight(); UpdateEstimate(); };
            startMinuteCombo.SelectionChanged += (s, e) => { SchedulePreflight(); UpdateEstimate(); };
            endHourCombo.SelectionChanged += (s, e) => { SchedulePreflight(); UpdateEstimate(); };
            endMinuteCombo.SelectionChanged += (s, e) => { SchedulePreflight(); UpdateEstimate(); };
        }

        private void OnModeChanged(object sender, SelectionChangedEventArgs e)
        {
            var mode = GetSelectedMode();
            continuousFields.Visibility = mode == TimelapseMode.Continuous ? Visibility.Visible : Visibility.Collapsed;
            eventFields.Visibility = mode == TimelapseMode.EventBased ? Visibility.Visible : Visibility.Collapsed;
            UpdateEstimate();
        }

        #endregion

        #region Time Presets

        private void OnTimePresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (timePresetCombo.SelectedItem is TimePresetOption preset && preset.Duration != TimeSpan.Zero)
            {
                var now = DateTime.Now;
                var start = now - preset.Duration;
                startDatePicker.SelectedDate = start.Date;
                endDatePicker.SelectedDate = now.Date;
                // Find closest hour/minute combo indices
                startHourCombo.SelectedIndex = start.Hour;
                endHourCombo.SelectedIndex = now.Hour;
                startMinuteCombo.SelectedIndex = start.Minute / 15;
                endMinuteCombo.SelectedIndex = now.Minute / 15;
                UpdateEstimate();
            }
        }

        #endregion

        #region Camera Management

        private void OnAddCameraClick(object sender, RoutedEventArgs e)
        {
            if (_cameras.Count >= 9)
            {
                MessageBox.Show("Maximum 9 cameras for stitch mode.", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var picker = new ItemPickerWpfWindow
                {
                    KindsFilter = new List<Guid> { Kind.Camera },
                    SelectionMode = SelectionModeOptions.AutoCloseOnSelect,
                    Items = Configuration.Instance.GetItemsByKind(Kind.Camera)
                };

                if (picker.ShowDialog() == true && picker.SelectedItems != null)
                {
                    foreach (var item in picker.SelectedItems)
                    {
                        if (_cameras.Any(c => c.Item.FQID.ObjectId == item.FQID.ObjectId))
                            continue;
                        _cameras.Add(new CameraEntry { Name = item.Name, Item = item });
                    }
                    UpdateCameraCount();
                    UpdateEstimate();
                }
            }
            catch (Exception ex)
            {
                idleHintText.Text = $"Camera picker error: {ex.Message}";
                idleHintText.Visibility = Visibility.Visible;
            }
        }

        private void OnRemoveCameraClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CameraEntry entry)
            {
                _cameras.Remove(entry);
                UpdateCameraCount();
                UpdateEstimate();
            }
        }

        private void OnClearCamerasClick(object sender, RoutedEventArgs e)
        {
            _cameras.Clear();
            UpdateCameraCount();
            UpdateEstimate();
        }

        private void UpdateCameraCount()
        {
            cameraCountText.Text = $"{_cameras.Count} camera(s) selected (max 9)";
        }

        #endregion

        #region Preflight

        private void SchedulePreflight()
        {
            _preflightDebounce?.Stop();
            _preflightDebounce?.Start();
        }

        private void SetPreflightLoading(bool loading)
        {
            preflightLoadingRow.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            idleClockIcon.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
            idleSpinnerIcon.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task RunPreflightAsync()
        {
            var range = GetTimeRange();
            if (range == null || _cameras.Count == 0)
            {
                Log.Info($"Preflight skipped (cameras={_cameras.Count}, range={(range == null ? "null" : "ok")})");
                preflightCard.Visibility = Visibility.Collapsed;
                SetPreflightLoading(false);
                _preflightSegments = new Dictionary<Guid, IReadOnlyList<RecordingSegment>>();
                UpdateEstimate();
                return;
            }

            _preflightCts?.Cancel();
            _preflightCts = new CancellationTokenSource();
            var token = _preflightCts.Token;

            var (start, end) = range.Value;
            var cameras = _cameras.ToList();

            Log.Info($"Preflight start: cams={cameras.Count} range={start:O}..{end:O}");

            // Show card + loading spinner immediately; keep any previous values visible until replaced.
            preflightCard.Visibility = Visibility.Visible;
            SetPreflightLoading(true);

            var perCam = new Dictionary<Guid, (string name, IReadOnlyList<RecordingSegment> segs)>();

            try
            {
                await Task.Run(() =>
                {
                    foreach (var cam in cameras)
                    {
                        token.ThrowIfCancellationRequested();
                        using (var q = new SequenceQuery(cam.Item))
                        {
                            var segs = q.GetRecordingSegments(start, end);
                            perCam[cam.Item.FQID.ObjectId] = (cam.Name, segs);
                        }
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                Log.Info("Preflight cancelled");
                // A newer query is running; let IT clear the spinner.
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Preflight failed", ex);
                SetPreflightLoading(false);
                preflightSeqCount.Text = "–";
                preflightRecorded.Text = ex.Message.Length > 40 ? ex.Message.Substring(0, 40) + "…" : ex.Message;
                preflightCoverage.Text = "error";
                return;
            }

            if (token.IsCancellationRequested) return;

            _preflightSegments = perCam.ToDictionary(p => p.Key, p => p.Value.segs);

            int totalSeq = 0;
            long totalTicks = 0;
            foreach (var p in perCam.Values)
            {
                totalSeq += p.segs.Count;
                foreach (var s in p.segs) totalTicks += s.Duration.Ticks;
            }
            var totalRecorded = TimeSpan.FromTicks(totalTicks);
            var totalWindow = (end - start);
            double coverage = 0;
            if (totalWindow.Ticks > 0 && perCam.Count > 0)
            {
                foreach (var p in perCam.Values)
                {
                    long camTicks = 0;
                    foreach (var s in p.segs) camTicks += s.Duration.Ticks;
                    var pct = (double)camTicks / totalWindow.Ticks;
                    if (pct > coverage) coverage = pct;
                }
            }

            SetPreflightLoading(false);
            preflightSeqCount.Text = totalSeq.ToString();
            preflightRecorded.Text = FormatDuration(totalRecorded.TotalSeconds);
            preflightCoverage.Text = $"{coverage * 100:F0}%";

            Log.Info($"Preflight done: totalSeq={totalSeq} totalRecorded={totalRecorded} coverageMax={coverage * 100:F1}%");
            foreach (var p in perCam.Values)
            {
                long camTicksLog = 0;
                foreach (var s in p.segs) camTicksLog += s.Duration.Ticks;
                Log.Info($"  cam='{p.name}' seq={p.segs.Count} recorded={TimeSpan.FromTicks(camTicksLog)}");
            }

            preflightPerCameraPanel.Children.Clear();
            foreach (var p in perCam.Values)
            {
                long camTicks = 0;
                foreach (var s in p.segs) camTicks += s.Duration.Ticks;
                var camDur = TimeSpan.FromTicks(camTicks);
                var line = new TextBlock
                {
                    Text = $"{p.name}: {p.segs.Count} seq · {FormatDuration(camDur.TotalSeconds)}",
                    Foreground = p.segs.Count == 0
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x5A, 0x5A))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                preflightPerCameraPanel.Children.Add(line);
            }

            UpdateEstimate();
        }

        #endregion

        #region Estimate

        private void UpdateEstimate()
        {
            var range = GetTimeRange();
            if (range == null || _cameras.Count == 0)
            {
                idleHintText.Visibility = Visibility.Visible;
                estimateCard.Visibility = Visibility.Collapsed;
                return;
            }

            var (start, end) = range.Value;
            var fps = GetSelectedFps();
            var mode = GetSelectedMode();
            var totalSpan = end - start;
            var resLabel = (resolutionCombo.SelectedItem as ResolutionOption)?.Label ?? "Original";

            // Pick timestamps the same way Generate will, using preflight segments if present.
            List<DateTime> timestamps = BuildTimestampsFromPreflight(mode, start, end);
            int frameCount = timestamps.Count;
            double durationSec = (double)frameCount / fps;

            idleHintText.Visibility = Visibility.Collapsed;
            estimateCard.Visibility = Visibility.Visible;

            estCameras.Text = $"{_cameras.Count}";
            estLayout.Text = GetLayoutDescription(_cameras.Count);
            estResolution.Text = resLabel;

            string intervalLabel = mode == TimelapseMode.Continuous
                ? (intervalCombo.SelectedItem as IntervalOption)?.Label ?? ""
                : $"{(eventIntervalCombo.SelectedItem as IntervalOption)?.Label}, max {GetMaxPerEvent()} / event";

            estTimeSpan.Text = $"{FormatDuration(totalSpan.TotalSeconds)}  ({intervalLabel})";
            estFrames.Text = $"~{frameCount}";
            estVideo.Text = $"{FormatDuration(durationSec)} @ {fps} FPS";
        }

        /// <summary>
        /// Produces the timestamp list that Generate will use, based on preflight segments.
        /// If preflight hasn't run yet, falls back to naive interval across the full range.
        /// </summary>
        private List<DateTime> BuildTimestampsFromPreflight(TimelapseMode mode, DateTime start, DateTime end)
        {
            var perCameraSegs = new List<IReadOnlyList<RecordingSegment>>();
            foreach (var cam in _cameras)
            {
                if (_preflightSegments.TryGetValue(cam.Item.FQID.ObjectId, out var segs))
                    perCameraSegs.Add(segs);
            }

            if (perCameraSegs.Count == 0)
            {
                // Preflight not yet available - approximate with naive interval
                var interval = mode == TimelapseMode.Continuous ? GetSelectedInterval() : GetSelectedEventInterval();
                return FrameGrabberService.GenerateTimestamps(start, end, interval);
            }

            var union = TimestampGenerator.Union(perCameraSegs);
            if (union.Count == 0) return new List<DateTime>();

            if (mode == TimelapseMode.Continuous)
                return TimestampGenerator.GenerateContinuous(union, GetSelectedInterval());

            return TimestampGenerator.GenerateEventBased(
                union,
                GetSelectedEventInterval(),
                GetMaxPerEvent(),
                GetMinPerEvent(),
                GetSelectedMergeGap());
        }

        #endregion

        #region Generation

        private async void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            if (_cameras.Count == 0)
            {
                MessageBox.Show("Please add at least one camera.", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var range = GetTimeRange();
            if (range == null)
            {
                MessageBox.Show("Invalid time range. End must be after start.", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (start, end) = range.Value;
            var mode = GetSelectedMode();
            var fps = GetSelectedFps();
            var scale = GetSelectedResolution();
            var maxWorkers = GetSelectedWorkers();
            var batchSize = GetSelectedBatchSize();
            var tsConfig = GetTimestampConfig();
            var mergeGap = GetSelectedMergeGap();

            // Ensure preflight has run (fetch segments for cameras that don't have them yet).
            var cameras = _cameras.ToList();
            var perCameraSegs = new Dictionary<Guid, IReadOnlyList<RecordingSegment>>();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var cam in cameras)
                    {
                        if (_preflightSegments.TryGetValue(cam.Item.FQID.ObjectId, out var s))
                        {
                            perCameraSegs[cam.Item.FQID.ObjectId] = s;
                        }
                        else
                        {
                            using (var q = new SequenceQuery(cam.Item))
                                perCameraSegs[cam.Item.FQID.ObjectId] = q.GetRecordingSegments(start, end);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to query recording sequences:\n\n{ex.Message}", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Union of all cameras' segments drives the shared wall-clock timeline.
            var union = TimestampGenerator.Union(perCameraSegs.Values.ToList());
            Log.Info($"Generate: mode={mode} cams={cameras.Count} range={start:O}..{end:O} unionSegments={union.Count}");
            if (union.Count == 0)
            {
                Log.Info("Generate aborted: union is empty");
                MessageBox.Show("No recordings found in the selected time range for any camera.", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<DateTime> timestamps = mode == TimelapseMode.Continuous
                ? TimestampGenerator.GenerateContinuous(union, GetSelectedInterval())
                : TimestampGenerator.GenerateEventBased(union, GetSelectedEventInterval(),
                    GetMaxPerEvent(), GetMinPerEvent(), mergeGap);

            Log.Info($"Generate: timestamps={timestamps.Count} interval={(mode == TimelapseMode.Continuous ? GetSelectedInterval() : GetSelectedEventInterval())} maxPerEvent={GetMaxPerEvent()} minPerEvent={GetMinPerEvent()} mergeGap={mergeGap}");

            if (timestamps.Count == 0)
            {
                Log.Info("Generate aborted: no timestamps after generation");
                MessageBox.Show("No frames in the selected time range.", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Stop any current playback
            mediaPlayer.Stop();
            mediaPlayer.Close();
            _playbackTimer.Stop();
            CleanupTempVideo();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // UI state: generating
            generateButton.IsEnabled = false;
            cancelButton.Visibility = Visibility.Visible;
            saveButton.Visibility = Visibility.Collapsed;
            playbackControls.Visibility = Visibility.Collapsed;
            mediaPlayer.Visibility = Visibility.Collapsed;
            idlePlaceholder.Visibility = Visibility.Collapsed;
            progressPanel.Visibility = Visibility.Visible;
            progressBar.Visibility = Visibility.Visible;
            progressBar.Maximum = timestamps.Count;
            progressBar.Value = 0;

            var tempPath = Path.Combine(Path.GetTempPath(), $"Timelapse_{Guid.NewGuid()}.mp4");
            var (gridCols, gridRows) = GetGridLayout(cameras.Count);

            // Pick a sample camera that actually has recordings for sizing.
            var sampleCam = cameras.FirstOrDefault(c =>
                perCameraSegs.TryGetValue(c.Item.FQID.ObjectId, out var s) && s.Count > 0)
                ?? cameras[0];
            var sampleTs = perCameraSegs[sampleCam.Item.FQID.ObjectId].Count > 0
                ? perCameraSegs[sampleCam.Item.FQID.ObjectId][0].Start
                : timestamps[0];

            try
            {
                // Get sample frame for resolution
                progressDetailText.Text = "Fetching sample frame...";
                progressStepText.Text = "Initializing...";
                progressDetailText.Text = "Fetching sample frame to determine resolution";

                int singleWidth = 0, singleHeight = 0;
                await Task.Run(() =>
                {
                    using (var src = new CameraFrameSource(sampleCam.Item))
                    {
                        var (frame, error) = src.GetFrame(sampleTs);
                        if (frame == null)
                            throw new Exception($"Could not fetch sample frame: {error}");
                        using (frame)
                        {
                            singleWidth = (int)(frame.Width * scale);
                            singleHeight = (int)(frame.Height * scale);
                        }
                    }
                });

                int totalWidth = singleWidth * gridCols;
                int totalHeight = singleHeight * gridRows;

                int workerCount = maxWorkers;
                int totalFrames = timestamps.Count;

                Log.Info($"Generate: canvas={totalWidth}x{totalHeight} cell={singleWidth}x{singleHeight} layout={gridCols}x{gridRows} fps={fps} workers={workerCount} batch={batchSize} totalFrames={totalFrames}");

                progressStepText.Text = $"Generating timelapse ({cameras.Count} camera(s), {GetLayoutDescription(cameras.Count)})";
                progressDetailText.Text = $"{totalWidth}x{totalHeight} @ {fps} FPS | {workerCount} workers, batch {batchSize}";

                // Per-camera segment arrays (indexed by camera position) for fast Covers() checks.
                var segsByIndex = new IReadOnlyList<RecordingSegment>[cameras.Count];
                for (int c = 0; c < cameras.Count; c++)
                {
                    segsByIndex[c] = perCameraSegs.TryGetValue(cameras[c].Item.FQID.ObjectId, out var s)
                        ? s : (IReadOnlyList<RecordingSegment>)Array.Empty<RecordingSegment>();
                }

                // Continuous mode keeps a "last valid frame" per camera so stale cells show
                // the previous scene dimmed. Timestamps are processed in order (we fetch in
                // parallel but encode in order), so we fill the cache sequentially on the encode
                // side rather than inside the Parallel.For.
                Bitmap[] lastKnownFrame = mode == TimelapseMode.Continuous
                    ? new Bitmap[cameras.Count] : null;

                await Task.Run(() =>
                {
                    using (var encoder = new TimelapseEncoder(totalWidth, totalHeight, fps, tempPath,
                        msg => Debug.WriteLine($"[Timelapse] {msg}")))
                    {
                        if (!encoder.Start())
                            throw new Exception("Failed to initialize FFmpeg encoder");

                        for (int batchStart = 0; batchStart < totalFrames; batchStart += batchSize)
                        {
                            token.ThrowIfCancellationRequested();

                            int batchEnd = Math.Min(batchStart + batchSize, totalFrames);
                            int batchLen = batchEnd - batchStart;

                            // Per-batch raw per-camera fetch results (one Bitmap per cell per timestamp)
                            // Layout: rawCells[timestampIndex][cameraIndex]
                            var rawCells = new Bitmap[batchLen][];

                            int fetchedInBatch = 0;
                            System.Threading.Tasks.Parallel.For(0, batchLen,
                                new ParallelOptions { MaxDegreeOfParallelism = workerCount, CancellationToken = token },
                                () =>
                                {
                                    var sources = new CameraFrameSource[cameras.Count];
                                    for (int c = 0; c < cameras.Count; c++)
                                        sources[c] = new CameraFrameSource(cameras[c].Item);
                                    return sources;
                                },
                                (j, state, localSources) =>
                                {
                                    var ts = timestamps[batchStart + j];
                                    var cells = new Bitmap[cameras.Count];
                                    for (int c = 0; c < cameras.Count; c++)
                                    {
                                        // Skip the server round-trip if we know this camera has no recording here.
                                        if (!TimestampGenerator.Covers(segsByIndex[c], ts))
                                            continue;

                                        var (frame, _) = localSources[c].GetFrame(ts);
                                        if (frame != null) cells[c] = frame;
                                    }
                                    rawCells[j] = cells;

                                    var fetched = Interlocked.Increment(ref fetchedInBatch);
                                    int frameNum = batchStart + fetched;
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        progressBar.Value = frameNum;
                                        detailProgressBar.Maximum = totalFrames;
                                        detailProgressBar.Value = frameNum;
                                        progressStepText.Text = $"Fetching frame {frameNum} / {totalFrames}";
                                        progressDetailText.Text = $"{ts:yyyy-MM-dd HH:mm:ss} | {workerCount} workers, batch {batchSize}";
                                    }));

                                    return localSources;
                                },
                                (localSources) =>
                                {
                                    foreach (var s in localSources)
                                        s?.Dispose();
                                });

                            // Compose frames sequentially so the "last known frame" cache reflects
                            // temporal order and isn't mutated concurrently.
                            var batchFrames = new Bitmap[batchLen];
                            for (int j = 0; j < batchLen; j++)
                            {
                                var ts = timestamps[batchStart + j];
                                var cells = rawCells[j] ?? new Bitmap[cameras.Count];

                                var canvas = new Bitmap(totalWidth, totalHeight);
                                using (var g = Graphics.FromImage(canvas))
                                {
                                    g.Clear(Color.Black);
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                                    for (int c = 0; c < cameras.Count; c++)
                                    {
                                        int col = c % gridCols;
                                        int row = c / gridCols;
                                        int x = col * singleWidth;
                                        int y = row * singleHeight;

                                        var live = cells[c];
                                        if (live != null)
                                        {
                                            g.DrawImage(live, x, y, singleWidth, singleHeight);

                                            if (mode == TimelapseMode.Continuous)
                                            {
                                                // Update last-known cache (clone; we'll dispose `live` below).
                                                lastKnownFrame[c]?.Dispose();
                                                lastKnownFrame[c] = (Bitmap)live.Clone();
                                            }
                                            live.Dispose();
                                        }
                                        else if (mode == TimelapseMode.Continuous && lastKnownFrame[c] != null)
                                        {
                                            // Stale: draw dimmed and overlay "no recording" badge.
                                            g.DrawImage(lastKnownFrame[c], x, y, singleWidth, singleHeight);
                                            using (var dim = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
                                                g.FillRectangle(dim, x, y, singleWidth, singleHeight);
                                            DrawStaleBadge(g, x, y, singleWidth, singleHeight, cameras[c].Name);
                                        }
                                        else
                                        {
                                            // Event-mode or never-seen camera in Continuous: black placeholder.
                                            DrawNoEventPlaceholder(g, x, y, singleWidth, singleHeight, cameras[c].Name);
                                        }
                                    }

                                    DrawTimestamp(g, totalWidth, totalHeight, ts, tsConfig);
                                }
                                batchFrames[j] = canvas;
                            }

                            // Encode batch sequentially (frames must be in order)
                            for (int j = 0; j < batchLen; j++)
                            {
                                token.ThrowIfCancellationRequested();
                                using (batchFrames[j])
                                {
                                    encoder.PushFrame(batchFrames[j]);
                                }
                                batchFrames[j] = null; // release reference immediately
                            }

                            // Update status after batch encode
                            int batchEndFrame = batchStart + batchLen;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                progressStepText.Text = $"Encoded {batchEndFrame} / {totalFrames}";
                            }));
                        }

                        encoder.Finish();
                    }

                    if (lastKnownFrame != null)
                    {
                        for (int i = 0; i < lastKnownFrame.Length; i++)
                        {
                            lastKnownFrame[i]?.Dispose();
                            lastKnownFrame[i] = null;
                        }
                    }
                }, token);

                _currentVideoPath = tempPath;
                Log.Info($"Generate complete: output='{tempPath}'");

                // Show playback
                progressPanel.Visibility = Visibility.Collapsed;
                mediaPlayer.Visibility = Visibility.Visible;
                playbackControls.Visibility = Visibility.Visible;
                saveButton.Visibility = Visibility.Visible;

                // Set source and start playback - use absolute file URI
                mediaPlayer.Source = new Uri(Path.GetFullPath(tempPath), UriKind.Absolute);
                mediaPlayer.Play();
                _playbackTimer.Start();
                playPauseButton.Content = "\u23F8"; // pause symbol

                // Done message will show in playback time area
            }
            catch (OperationCanceledException)
            {
                Log.Info("Generate cancelled by user");
                progressDetailText.Text = "Generation cancelled.";
                progressPanel.Visibility = Visibility.Collapsed;
                idlePlaceholder.Visibility = Visibility.Visible;
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
            catch (Exception ex)
            {
                Log.Error("Generate failed", ex);
                progressDetailText.Text = "Generation failed.";
                progressPanel.Visibility = Visibility.Collapsed;
                idlePlaceholder.Visibility = Visibility.Visible;
                MessageBox.Show($"Timelapse generation failed:\n\n{ex.Message}", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
            finally
            {
                generateButton.IsEnabled = true;
                cancelButton.Visibility = Visibility.Collapsed;
                progressBar.Visibility = Visibility.Collapsed;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void OnClosePreviewClick(object sender, RoutedEventArgs e)
        {
            // Stop playback and release any file lock on the temp video.
            mediaPlayer.Stop();
            mediaPlayer.Close();
            mediaPlayer.Source = null;
            _playbackTimer?.Stop();
            playPauseButton.Content = "▶";
            seekSlider.Value = 0;
            seekSlider.IsEnabled = false;
            playbackTimeText.Text = "00:00 / 00:00";

            // Switch back to the configuration / idle view.
            mediaPlayer.Visibility = Visibility.Collapsed;
            playbackControls.Visibility = Visibility.Collapsed;
            saveButton.Visibility = Visibility.Collapsed;
            idlePlaceholder.Visibility = Visibility.Visible;

            CleanupTempVideo();
            UpdateEstimate();
        }

        #endregion

        #region Save

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_currentVideoPath == null || !File.Exists(_currentVideoPath))
            {
                MessageBox.Show("No video to save. Generate a timelapse first.", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "MP4 Video (*.mp4)|*.mp4",
                FileName = $"Timelapse_{DateTime.Now:yyyy-MM-dd_HHmmss}",
                DefaultExt = ".mp4",
                Title = "Save Timelapse Video"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                // Pause playback to release file lock
                mediaPlayer.Pause();
                _playbackTimer.Stop();

                File.Copy(_currentVideoPath, dlg.FileName, true);
                // Saved confirmation shown via MessageBox already

                var result = MessageBox.Show("Video saved! Open in default player?", "Timelapse",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed:\n\n{ex.Message}", "Timelapse",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Playback Controls

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (mediaPlayer.Source == null) return;

            if (playPauseButton.Content.ToString() == "\u25B6") // play
            {
                mediaPlayer.Play();
                _playbackTimer.Start();
                playPauseButton.Content = "\u23F8";
            }
            else // pause
            {
                mediaPlayer.Pause();
                _playbackTimer.Stop();
                playPauseButton.Content = "\u25B6";
            }
        }

        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Stop();
            _playbackTimer.Stop();
            playPauseButton.Content = "\u25B6";
            seekSlider.Value = 0;
        }

        private void OnSeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSeeking && mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = mediaPlayer.NaturalDuration.TimeSpan;
                var pos = TimeSpan.FromSeconds(duration.TotalSeconds * (seekSlider.Value / 100.0));
                mediaPlayer.Position = pos;
                playbackTimeText.Text = $"{FormatTimeSpan(pos)} / {FormatTimeSpan(duration)}";
            }
        }

        private void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            seekSlider.IsEnabled = true;
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                var dur = mediaPlayer.NaturalDuration.TimeSpan;
                playbackTimeText.Text = $"00:00 / {FormatTimeSpan(dur)}";
            }
        }

        private void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            // Stop then Pause to prevent auto-replay
            mediaPlayer.Stop();
            mediaPlayer.Position = TimeSpan.Zero;
            _playbackTimer.Stop();
            playPauseButton.Content = "\u25B6";
            seekSlider.Value = 0;
            if (mediaPlayer.NaturalDuration.HasTimeSpan)
                playbackTimeText.Text = $"00:00 / {FormatTimeSpan(mediaPlayer.NaturalDuration.TimeSpan)}";
        }

        private void OnPlaybackTimerTick(object sender, EventArgs e)
        {
            if (mediaPlayer.NaturalDuration.HasTimeSpan && !_isSeeking)
            {
                var pos = mediaPlayer.Position;
                var dur = mediaPlayer.NaturalDuration.TimeSpan;
                if (dur.TotalSeconds > 0)
                    seekSlider.Value = (pos.TotalSeconds / dur.TotalSeconds) * 100.0;
                playbackTimeText.Text = $"{FormatTimeSpan(pos)} / {FormatTimeSpan(dur)}";
            }
        }

        #endregion

        #region Helpers

        private (DateTime Start, DateTime End)? GetTimeRange()
        {
            if (startDatePicker.SelectedDate == null || endDatePicker.SelectedDate == null)
                return null;
            if (startHourCombo.SelectedItem == null || startMinuteCombo.SelectedItem == null)
                return null;
            if (endHourCombo.SelectedItem == null || endMinuteCombo.SelectedItem == null)
                return null;

            var startDate = startDatePicker.SelectedDate.Value;
            var endDate = endDatePicker.SelectedDate.Value;
            int startHour = int.Parse((string)startHourCombo.SelectedItem);
            int startMin = int.Parse((string)startMinuteCombo.SelectedItem);
            int endHour = int.Parse((string)endHourCombo.SelectedItem);
            int endMin = int.Parse((string)endMinuteCombo.SelectedItem);

            var start = startDate.Date.AddHours(startHour).AddMinutes(startMin);
            var end = endDate.Date.AddHours(endHour).AddMinutes(endMin);

            if (end <= start) return null;
            return (start, end);
        }

        private TimelapseMode GetSelectedMode()
        {
            return (modeCombo?.SelectedItem as ModeOption)?.Mode ?? TimelapseMode.Continuous;
        }

        private TimeSpan GetSelectedInterval()
        {
            return (intervalCombo.SelectedItem as IntervalOption)?.Interval ?? TimeSpan.FromMinutes(1);
        }

        private TimeSpan GetSelectedEventInterval()
        {
            return (eventIntervalCombo?.SelectedItem as IntervalOption)?.Interval ?? TimeSpan.FromSeconds(10);
        }

        private int GetMaxPerEvent()
        {
            return (maxPerEventCombo?.SelectedItem as CountOption)?.Count ?? 10;
        }

        private int GetMinPerEvent()
        {
            return (minPerEventCombo?.SelectedItem as CountOption)?.Count ?? 1;
        }

        private TimeSpan GetSelectedMergeGap()
        {
            return (mergeGapCombo?.SelectedItem as GapOption)?.Gap ?? TimeSpan.FromSeconds(2);
        }

        private int GetSelectedFps()
        {
            return (fpsCombo.SelectedItem as FpsOption)?.Fps ?? 24;
        }

        private double GetSelectedResolution()
        {
            return (resolutionCombo.SelectedItem as ResolutionOption)?.Scale ?? 1.0;
        }

        private int GetSelectedWorkers()
        {
            return (workersCombo.SelectedItem as WorkersOption)?.Count ?? 5;
        }

        private int GetSelectedBatchSize()
        {
            return (batchSizeCombo.SelectedItem as BatchSizeOption)?.Size ?? 50;
        }

        private TimestampConfig GetTimestampConfig()
        {
            return new TimestampConfig
            {
                Enabled = showTimestampCheck.IsChecked == true,
                Position = (string)tsPositionCombo.SelectedItem ?? "Bottom-Left",
                Format = (tsFormatCombo.SelectedItem as TsFormatOption)?.Format ?? "yyyy-MM-dd HH:mm:ss",
                TextColor = (tsColorCombo.SelectedItem as TsColorOption)?.Color ?? Color.White,
                BgColor = (tsBgCombo.SelectedItem as TsBgOption)?.Color ?? Color.FromArgb(160, 0, 0, 0),
                FontSize = (tsFontSizeCombo.SelectedItem as TsFontSizeOption)?.Size ?? 22f,
            };
        }

        /// <summary>
        /// Draws timestamp overlay on a composed frame canvas.
        /// </summary>
        internal static void DrawTimestamp(Graphics g, int canvasWidth, int canvasHeight,
            DateTime timestamp, TimestampConfig cfg)
        {
            if (!cfg.Enabled) return;

            var text = timestamp.ToString(cfg.Format);
            using (var font = new Font("Segoe UI", cfg.FontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var textSize = g.MeasureString(text, font);
                int pad = (int)(cfg.FontSize * 0.4f);
                float x, y;

                switch (cfg.Position)
                {
                    case "Top-Left":
                        x = pad;
                        y = pad;
                        break;
                    case "Top-Right":
                        x = canvasWidth - textSize.Width - pad;
                        y = pad;
                        break;
                    case "Bottom-Right":
                        x = canvasWidth - textSize.Width - pad;
                        y = canvasHeight - textSize.Height - pad;
                        break;
                    default: // Bottom-Left
                        x = pad;
                        y = canvasHeight - textSize.Height - pad;
                        break;
                }

                // Draw background rectangle
                if (cfg.BgColor != Color.Empty)
                {
                    using (var bgBrush = new SolidBrush(cfg.BgColor))
                    {
                        g.FillRectangle(bgBrush, x - pad / 2f, y - pad / 2f,
                            textSize.Width + pad, textSize.Height + pad);
                    }
                }

                // Draw text
                using (var brush = new SolidBrush(cfg.TextColor))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.DrawString(text, font, brush, x, y);
                }
            }
        }

        /// <summary>
        /// Continuous-mode "stale" overlay: shows the camera name in the top-left of a dimmed cell.
        /// </summary>
        private static void DrawStaleBadge(Graphics g, int x, int y, int w, int h, string cameraName)
        {
            float fontSize = Math.Max(10f, Math.Min(w, h) * 0.04f);
            using (var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            using (var bgBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            {
                var label = $"{cameraName} · no recording";
                var size = g.MeasureString(label, font);
                int pad = 4;
                g.FillRectangle(bgBrush, x + pad, y + pad, size.Width + pad * 2, size.Height + pad);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.DrawString(label, font, textBrush, x + pad * 2, y + pad + pad / 2f);
            }
        }

        /// <summary>
        /// Event-mode "nothing here" cell: black fill plus camera name + "no event" centered.
        /// </summary>
        private static void DrawNoEventPlaceholder(Graphics g, int x, int y, int w, int h, string cameraName)
        {
            using (var bg = new SolidBrush(Color.Black))
                g.FillRectangle(bg, x, y, w, h);

            float fontSize = Math.Max(12f, Math.Min(w, h) * 0.05f);
            using (var nameFont = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subFont = new Font("Segoe UI", fontSize * 0.75f, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brushName = new SolidBrush(Color.FromArgb(220, 200, 200, 200)))
            using (var brushSub = new SolidBrush(Color.FromArgb(180, 120, 120, 120)))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                var nameSize = g.MeasureString(cameraName ?? "", nameFont);
                var subText = "no event";
                var subSize = g.MeasureString(subText, subFont);

                float cx = x + w / 2f;
                float cy = y + h / 2f;
                g.DrawString(cameraName ?? "", nameFont, brushName, cx - nameSize.Width / 2f, cy - nameSize.Height);
                g.DrawString(subText, subFont, brushSub, cx - subSize.Width / 2f, cy + 2);
            }
        }

        /// <summary>
        /// Determines the grid layout for stitching cameras.
        /// Returns (columns, rows).
        /// </summary>
        internal static (int Cols, int Rows) GetGridLayout(int cameraCount)
        {
            switch (cameraCount)
            {
                case 1: return (1, 1);
                case 2: return (2, 1);
                case 3: return (3, 1);
                case 4: return (2, 2);
                case 5:
                case 6: return (3, 2);
                case 7:
                case 8:
                case 9: return (3, 3);
                default: return (1, 1);
            }
        }

        private static string GetLayoutDescription(int count)
        {
            var (cols, rows) = GetGridLayout(count);
            return $"{cols}x{rows}";
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 60) return $"{seconds:F0}s";
            if (seconds < 3600) return $"{seconds / 60:F1} min";
            return $"{seconds / 3600:F1} hr";
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private void CleanupTempVideo()
        {
            if (_currentVideoPath != null && File.Exists(_currentVideoPath))
            {
                try { File.Delete(_currentVideoPath); } catch { }
                _currentVideoPath = null;
            }
        }

        #endregion
    }

    #region Data Models

    internal class CameraEntry : INotifyPropertyChanged
    {
        private string _name;
        private Item _item;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public Item Item
        {
            get => _item;
            set { _item = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    internal enum TimelapseMode { Continuous, EventBased }

    internal class ModeOption
    {
        public string Label { get; }
        public TimelapseMode Mode { get; }
        public ModeOption(string label, TimelapseMode mode) { Label = label; Mode = mode; }
        public override string ToString() => Label;
    }

    internal class IntervalOption
    {
        public string Label { get; }
        public TimeSpan Interval { get; }
        public IntervalOption(string label, TimeSpan interval) { Label = label; Interval = interval; }
        public override string ToString() => Label;
    }

    internal class CountOption
    {
        public int Count { get; }
        public CountOption(int count) { Count = count; }
        public override string ToString() => $"{Count}";
    }

    internal class GapOption
    {
        public string Label { get; }
        public TimeSpan Gap { get; }
        public GapOption(string label, TimeSpan gap) { Label = label; Gap = gap; }
        public override string ToString() => Label;
    }

    internal class FpsOption
    {
        public int Fps { get; }
        public FpsOption(int fps) { Fps = fps; }
        public override string ToString() => $"{Fps} FPS";
    }

    internal class ResolutionOption
    {
        public string Label { get; }
        public double Scale { get; }
        public ResolutionOption(string label, double scale) { Label = label; Scale = scale; }
        public override string ToString() => Label;
    }

    internal class WorkersOption
    {
        public int Count { get; }
        public WorkersOption(int count) { Count = count; }
        public override string ToString() => $"{Count} worker{(Count > 1 ? "s" : "")}";
    }

    internal class BatchSizeOption
    {
        public int Size { get; }
        public BatchSizeOption(int size) { Size = size; }
        public override string ToString() => $"{Size} frames";
    }

    internal class TimePresetOption
    {
        public string Label { get; }
        public TimeSpan Duration { get; }
        public TimePresetOption(string label, TimeSpan duration) { Label = label; Duration = duration; }
        public override string ToString() => Label;
    }

    internal class TsFormatOption
    {
        public string Label { get; }
        public string Format { get; }
        public TsFormatOption(string label, string format) { Label = label; Format = format; }
        public override string ToString() => Label;
    }

    internal class TsColorOption
    {
        public string Label { get; }
        public Color Color { get; }
        public TsColorOption(string label, Color color) { Label = label; Color = color; }
        public override string ToString() => Label;
    }

    internal class TsBgOption
    {
        public string Label { get; }
        public Color Color { get; }
        public TsBgOption(string label, Color color) { Label = label; Color = color; }
        public override string ToString() => Label;
    }

    internal class TsFontSizeOption
    {
        public string Label { get; }
        public float Size { get; }
        public TsFontSizeOption(string label, float size) { Label = label; Size = size; }
        public override string ToString() => Label;
    }

    internal struct TimestampConfig
    {
        public bool Enabled;
        public string Position;
        public string Format;
        public Color TextColor;
        public Color BgColor;
        public float FontSize;
    }

    #endregion
}
