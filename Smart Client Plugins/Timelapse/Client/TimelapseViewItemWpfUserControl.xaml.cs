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
using Microsoft.Win32;
using Timelapse.Services;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.UI;

namespace Timelapse.Client
{
    public partial class TimelapseViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly ObservableCollection<CameraEntry> _cameras = new ObservableCollection<CameraEntry>();
        private CancellationTokenSource _cts;
        private string _currentVideoPath;
        private DispatcherTimer _playbackTimer;
        private bool _isSeeking;
        private bool _wasPlayingBeforeSeek;

        public TimelapseViewItemWpfUserControl()
        {
            InitializeComponent();
            cameraListBox.ItemsSource = _cameras;
            InitializeControls();
        }

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

            // Frame interval options
            intervalCombo.Items.Add(new IntervalOption("Every 10 seconds", TimeSpan.FromSeconds(10)));
            intervalCombo.Items.Add(new IntervalOption("Every 30 seconds", TimeSpan.FromSeconds(30)));
            intervalCombo.Items.Add(new IntervalOption("Every 1 minute", TimeSpan.FromMinutes(1)));
            intervalCombo.Items.Add(new IntervalOption("Every 5 minutes", TimeSpan.FromMinutes(5)));
            intervalCombo.Items.Add(new IntervalOption("Every 10 minutes", TimeSpan.FromMinutes(10)));
            intervalCombo.Items.Add(new IntervalOption("Every 15 minutes", TimeSpan.FromMinutes(15)));
            intervalCombo.Items.Add(new IntervalOption("Every 30 minutes", TimeSpan.FromMinutes(30)));
            intervalCombo.Items.Add(new IntervalOption("Every 1 hour", TimeSpan.FromHours(1)));
            intervalCombo.SelectedIndex = 2; // 1 minute

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

            // Update estimate when settings change
            intervalCombo.SelectionChanged += (s, e) => UpdateEstimate();
            fpsCombo.SelectionChanged += (s, e) => UpdateEstimate();
            startDatePicker.SelectedDateChanged += (s, e) => UpdateEstimate();
            endDatePicker.SelectedDateChanged += (s, e) => UpdateEstimate();
            startHourCombo.SelectionChanged += (s, e) => UpdateEstimate();
            startMinuteCombo.SelectionChanged += (s, e) => UpdateEstimate();
            endHourCombo.SelectionChanged += (s, e) => UpdateEstimate();
            endMinuteCombo.SelectionChanged += (s, e) => UpdateEstimate();
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
                statusText.Text = $"Camera picker error: {ex.Message}";
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

        #region Estimate

        private void UpdateEstimate()
        {
            var range = GetTimeRange();
            if (range == null || _cameras.Count == 0)
            {
                estimateText.Text = "Select cameras and time range";
                return;
            }

            var (start, end) = range.Value;
            var interval = GetSelectedInterval();
            var fps = GetSelectedFps();
            var timestamps = FrameGrabberService.GenerateTimestamps(start, end, interval);
            int frameCount = timestamps.Count;
            double durationSec = (double)frameCount / fps;

            estimateText.Text = $"~{frameCount} frames, {FormatDuration(durationSec)} video\n" +
                                $"Layout: {GetLayoutDescription(_cameras.Count)}";
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
            var interval = GetSelectedInterval();
            var fps = GetSelectedFps();
            var scale = GetSelectedResolution();
            var maxWorkers = GetSelectedWorkers();
            var batchSize = GetSelectedBatchSize();
            var timestamps = FrameGrabberService.GenerateTimestamps(start, end, interval);

            if (timestamps.Count == 0)
            {
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
            var cameras = _cameras.ToList();
            var (gridCols, gridRows) = GetGridLayout(cameras.Count);

            try
            {
                // Check recordings exist
                statusText.Text = "Checking recordings...";
                progressStepText.Text = "Checking recordings...";
                progressDetailText.Text = "Verifying cameras have recorded data";

                var hasRecordings = await Task.Run(() =>
                {
                    foreach (var cam in cameras)
                    {
                        using (var src = new CameraFrameSource(cam.Item))
                        {
                            if (!src.HasRecordings())
                                return (false, cam.Name);
                        }
                    }
                    return (true, (string)null);
                });

                if (!hasRecordings.Item1)
                {
                    MessageBox.Show($"No recordings found for camera: {hasRecordings.Item2}", "Timelapse",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get sample frame for resolution
                statusText.Text = "Fetching sample frame...";
                progressStepText.Text = "Initializing...";
                progressDetailText.Text = "Fetching sample frame to determine resolution";

                int singleWidth = 0, singleHeight = 0;
                await Task.Run(() =>
                {
                    using (var src = new CameraFrameSource(cameras[0].Item))
                    {
                        var (frame, error) = src.GetFrame(timestamps[0]);
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

                statusText.Text = $"Encoding {totalWidth}x{totalHeight} @ {fps} FPS...";
                progressStepText.Text = $"Generating timelapse ({cameras.Count} camera(s), {GetLayoutDescription(cameras.Count)})";

                int workerCount = maxWorkers;
                int totalFrames = timestamps.Count;

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
                            var batchFrames = new Bitmap[batchLen];

                            // Fetch frames in parallel within the batch
                            // Each worker gets its own CameraFrameSource per camera (thread-local)
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
                                    var canvas = new Bitmap(totalWidth, totalHeight);
                                    using (var g = Graphics.FromImage(canvas))
                                    {
                                        g.Clear(Color.Black);
                                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                                        for (int c = 0; c < cameras.Count; c++)
                                        {
                                            var (frame, error) = localSources[c].GetFrame(ts);
                                            if (frame != null)
                                            {
                                                int col = c % gridCols;
                                                int row = c / gridCols;
                                                using (frame)
                                                {
                                                    g.DrawImage(frame, col * singleWidth, row * singleHeight,
                                                        singleWidth, singleHeight);
                                                }
                                            }
                                        }
                                    }
                                    batchFrames[j] = canvas;

                                    // Update fetch progress
                                    var fetched = Interlocked.Increment(ref fetchedInBatch);
                                    int frameNum = batchStart + fetched;
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        progressBar.Value = frameNum;
                                        detailProgressBar.Maximum = totalFrames;
                                        detailProgressBar.Value = frameNum;
                                        progressDetailText.Text = $"Fetching {frameNum} / {totalFrames} - {ts:yyyy-MM-dd HH:mm:ss}";
                                        statusText.Text = $"Fetching frame {frameNum}/{totalFrames} ({workerCount} workers)...";
                                    }));

                                    return localSources;
                                },
                                (localSources) =>
                                {
                                    foreach (var s in localSources)
                                        s?.Dispose();
                                });

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
                                statusText.Text = $"Encoded {batchEndFrame}/{totalFrames} ({workerCount} workers, batch {batchSize})...";
                            }));
                        }

                        encoder.Finish();
                    }
                }, token);

                _currentVideoPath = tempPath;

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

                statusText.Text = $"Done! {timestamps.Count} frames encoded.";
            }
            catch (OperationCanceledException)
            {
                statusText.Text = "Generation cancelled.";
                progressPanel.Visibility = Visibility.Collapsed;
                idlePlaceholder.Visibility = Visibility.Visible;
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
            catch (Exception ex)
            {
                statusText.Text = "Generation failed.";
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
                statusText.Text = $"Saved: {dlg.FileName}";

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
            _playbackTimer.Stop();
            playPauseButton.Content = "\u25B6";
            mediaPlayer.Position = TimeSpan.Zero;
            seekSlider.Value = 0;
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

        private TimeSpan GetSelectedInterval()
        {
            return (intervalCombo.SelectedItem as IntervalOption)?.Interval ?? TimeSpan.FromMinutes(1);
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

    internal class IntervalOption
    {
        public string Label { get; }
        public TimeSpan Interval { get; }
        public IntervalOption(string label, TimeSpan interval) { Label = label; Interval = interval; }
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

    #endregion
}
