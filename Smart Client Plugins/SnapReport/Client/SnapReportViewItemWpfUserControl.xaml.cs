using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapReport.Services;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.ConfigurationItems;

namespace SnapReport.Client
{
    public partial class SnapReportViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private List<CameraTreeNode> _rootNodes;
        private Stopwatch _generationTimer;
        private DispatcherTimer _elapsedTimer;

        public SnapReportViewItemWpfUserControl()
        {
            InitializeComponent();
        }

        public override void Init()
        {
            PopulateCameraTree();
        }

        public override void Close()
        {
        }

        private void PopulateCameraTree()
        {
            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                var mgmt = new ManagementServer(EnvironmentManager.Instance.MasterSite);

                _rootNodes = new List<CameraTreeNode>();
                foreach (var group in mgmt.CameraGroupFolder.CameraGroups)
                {
                    var node = BuildCameraGroupNode(group, serverId);
                    if (node != null)
                        _rootNodes.Add(node);
                }

                BuildTreeItems(_rootNodes, cameraTree.Items);

                UpdateStatus();
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error loading cameras: {ex.Message}";
            }
        }

        private CameraTreeNode BuildCameraGroupNode(CameraGroup group, ServerId serverId)
        {
            var node = new CameraTreeNode
            {
                Name = group.Name,
                IsFolder = true,
                IsChecked = true,
                Children = new List<CameraTreeNode>()
            };

            // Add cameras in this group
            foreach (var cam in group.CameraFolder.Cameras)
            {
                if (!cam.Enabled) continue;
                var cameraId = new Guid(cam.Id);
                var item = Configuration.Instance.GetItem(serverId, cameraId, Kind.Camera);
                if (item == null) continue;

                node.Children.Add(new CameraTreeNode
                {
                    Name = cam.Name,
                    IsFolder = false,
                    CameraItem = item,
                    IsChecked = true,
                });
            }

            // Recurse into sub-groups
            foreach (var subGroup in group.CameraGroupFolder.CameraGroups)
            {
                var childNode = BuildCameraGroupNode(subGroup, serverId);
                if (childNode != null)
                    node.Children.Add(childNode);
            }

            // Skip empty groups
            if (node.Children.Count == 0)
                return null;

            return node;
        }

        private void BuildTreeItems(List<CameraTreeNode> nodes, ItemCollection items)
        {
            foreach (var node in nodes)
            {
                var item = new TreeViewItem();
                var checkBox = new CheckBox
                {
                    Content = node.Name,
                    IsChecked = node.IsChecked,
                    IsThreeState = node.IsFolder,
                    Foreground = System.Windows.Media.Brushes.White,
                    Tag = node,
                    Margin = new Thickness(2),
                };
                checkBox.Checked += OnCameraCheckChanged;
                checkBox.Unchecked += OnCameraCheckChanged;
                item.Header = checkBox;
                item.IsExpanded = true;

                if (node.IsFolder && node.Children != null)
                {
                    BuildTreeItems(node.Children, item.Items);
                }

                items.Add(item);
            }
        }

        private void OnCameraCheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is CameraTreeNode node)
            {
                node.IsChecked = cb.IsChecked == true;

                // If folder, cascade to children
                if (node.IsFolder && node.Children != null)
                {
                    foreach (var child in node.Children)
                        child.IsChecked = node.IsChecked;

                    // Update child checkboxes in the tree
                    if (cb.Parent is TreeViewItem tvi)
                        UpdateChildCheckboxes(tvi, node.IsChecked);
                }

                // Update parent folder state
                UpdateParentCheckState(cb);
                UpdateStatus();
            }
        }

        private void UpdateChildCheckboxes(TreeViewItem parent, bool isChecked)
        {
            foreach (var item in parent.Items)
            {
                if (item is TreeViewItem childTvi && childTvi.Header is CheckBox childCb)
                {
                    childCb.IsChecked = isChecked;
                }
            }
        }

        private void UpdateParentCheckState(CheckBox childCb)
        {
            var childTvi = childCb.Parent as TreeViewItem;
            if (childTvi == null) return;

            var parentTvi = childTvi.Parent as TreeViewItem;
            if (parentTvi == null) return;

            if (parentTvi.Header is CheckBox parentCb && parentCb.Tag is CameraTreeNode parentNode && parentNode.IsFolder)
            {
                bool allChecked = parentNode.Children.All(c => c.IsChecked);
                bool anyChecked = parentNode.Children.Any(c => c.IsChecked);

                parentCb.IsChecked = allChecked ? true : (anyChecked ? (bool?)null : false);
                parentNode.IsChecked = anyChecked;
            }
        }

        private List<CameraTreeNode> GetCheckedCameras()
        {
            var result = new List<CameraTreeNode>();
            if (_rootNodes == null) return result;
            CollectCheckedCameras(_rootNodes, result);
            return result;
        }

        private void CollectCheckedCameras(List<CameraTreeNode> nodes, List<CameraTreeNode> result)
        {
            foreach (var node in nodes)
            {
                if (node.IsFolder && node.Children != null)
                {
                    CollectCheckedCameras(node.Children, result);
                }
                else if (node.IsChecked && node.CameraItem != null)
                {
                    result.Add(node);
                }
            }
        }

        private void UpdateStatus()
        {
            var checked_ = GetCheckedCameras();
            statusText.Text = $"{checked_.Count} camera(s) selected";
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            SetAllChecked(true);
        }

        private void OnDeselectAllClick(object sender, RoutedEventArgs e)
        {
            SetAllChecked(false);
        }

        private void SetAllChecked(bool isChecked)
        {
            if (_rootNodes == null) return;
            foreach (var root in _rootNodes)
            {
                root.IsChecked = isChecked;
                if (root.Children != null)
                    foreach (var child in root.Children)
                        child.IsChecked = isChecked;
            }

            foreach (var item in cameraTree.Items)
            {
                if (item is TreeViewItem tvi)
                    SetTreeItemChecked(tvi, isChecked);
            }
            UpdateStatus();
        }

        private void SetTreeItemChecked(TreeViewItem tvi, bool isChecked)
        {
            if (tvi.Header is CheckBox cb)
                cb.IsChecked = isChecked;
            foreach (var child in tvi.Items)
            {
                if (child is TreeViewItem childTvi)
                    SetTreeItemChecked(childTvi, isChecked);
            }
        }

        private static BitmapImage BytesToBitmap(byte[] data)
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(data))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
        }

        private void OnSplitToggleChanged(object sender, RoutedEventArgs e)
        {
            if (splitSizeBox != null)
                splitSizeBox.IsEnabled = splitToggle.IsChecked == true;
        }

        private async void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            var cameras = GetCheckedCameras();
            if (cameras.Count == 0)
            {
                MessageBox.Show("Please select at least one camera.", "SnapReport",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Resolve split settings
            long? maxBytesPerPart = null;
            bool splitEnabled = splitToggle.IsChecked == true;
            if (splitEnabled)
            {
                if (!int.TryParse(splitSizeBox.Text, out int mb) || mb < 1)
                {
                    MessageBox.Show("Split size must be a whole number of megabytes (1 or higher).",
                        "SnapReport", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                maxBytesPerPart = mb * 1024L * 1024L;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"SnapReport_{DateTime.Now:yyyy-MM-dd}",
                DefaultExt = ".pdf",
                Title = "Save SnapReport PDF"
            };
            if (dlg.ShowDialog() != true) return;

            var outputPath = dlg.FileName;
            generateButton.IsEnabled = false;

            // Show snapshot gallery, hide placeholder
            var previews = new ObservableCollection<SnapshotPreviewItem>();
            snapshotList.ItemsSource = previews;
            idlePlaceholder.Visibility = Visibility.Collapsed;
            snapshotScroll.Visibility = Visibility.Visible;
            StartProgress(cameras.Count);

            var entries = new List<CameraReportEntry>();
            string tempDir = Path.Combine(Path.GetTempPath(), "SnapReport_" + Guid.NewGuid());

            try
            {
                Directory.CreateDirectory(tempDir);

                int completed = 0;
                int total = cameras.Count;

                foreach (var cam in cameras)
                {
                    byte[] imageData = null;
                    DateTime snapTime = DateTime.MinValue;
                    string error = null;

                    try
                    {
                        var snapshot = await Task.Run(() => SnapshotService.GrabSnapshot(cam.CameraItem));
                        imageData = snapshot.ImageData;
                        snapTime = snapshot.Timestamp;
                        error = snapshot.Error;
                    }
                    catch (Exception ex)
                    {
                        error = $"Capture failed: {ex.Message}";
                    }

                    entries.Add(new CameraReportEntry
                    {
                        CameraName = cam.Name,
                        ImageData = imageData,
                        Timestamp = snapTime != DateTime.MinValue ? snapTime : DateTime.UtcNow,
                        ErrorMessage = error,
                    });

                    // Build thumbnail on UI thread (BitmapImage requires STA)
                    BitmapImage thumbnail = null;
                    if (imageData != null)
                    {
                        try
                        {
                            thumbnail = BytesToBitmap(imageData);
                        }
                        catch
                        {
                            // Thumbnail failed - not critical, PDF still gets the raw bytes
                        }
                    }

                    completed++;
                    previews.Add(new SnapshotPreviewItem
                    {
                        CameraName = cam.Name,
                        Thumbnail = thumbnail,
                        ErrorMessage = error,
                        HasImage = thumbnail != null,
                        HasError = error != null,
                    });
                    UpdateProgress(completed, total, cam.Name);
                    statusText.Text = $"Captured: {cam.Name}";
                    snapshotScroll.ScrollToEnd();
                }

                statusText.Text = "Generating PDF...";
                progressCurrent.Text = "Generating PDF...";

                List<string> producedFiles;
                try
                {
                    producedFiles = await Task.Run(() => PdfReportService.GenerateReport(outputPath, entries, maxBytesPerPart));
                }
                catch (Exception ex)
                {
                    statusText.Text = "PDF generation failed";
                    MessageBox.Show($"Failed to generate PDF:\n\n{ex.Message}", "SnapReport",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (producedFiles.Count == 1)
                {
                    statusText.Text = $"Saved: {producedFiles[0]}";
                    progressCount.Text = $"{total} / {total} - Done";

                    try
                    {
                        var result = MessageBox.Show("Report generated successfully!\n\nOpen the PDF now?",
                            "SnapReport", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (result == MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo(producedFiles[0]) { UseShellExecute = true });
                        }
                    }
                    catch { }
                }
                else
                {
                    string folder = Path.GetDirectoryName(producedFiles[0]);
                    statusText.Text = $"Saved {producedFiles.Count} files to {folder}";
                    progressCount.Text = $"{total} / {total} - Done ({producedFiles.Count} parts)";

                    try
                    {
                        var result = MessageBox.Show(
                            $"Report split into {producedFiles.Count} files.\n\nOpen output folder?",
                            "SnapReport", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (result == MessageBoxResult.Yes && folder != null)
                        {
                            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                statusText.Text = "Generation failed";
                MessageBox.Show($"Failed to generate report:\n\n{ex.Message}", "SnapReport",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }

                generateButton.IsEnabled = true;
                StopProgress();
            }
        }

        private void StartProgress(int total)
        {
            progressPanel.Visibility = Visibility.Visible;
            progressPercent.Text = "0%";
            progressCurrent.Text = "Starting capture...";
            progressCount.Text = $"0 / {total}";
            progressElapsed.Text = "0s";
            progressFill.Width = 0;

            _generationTimer = Stopwatch.StartNew();
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _elapsedTimer.Tick += (s, e) =>
            {
                if (_generationTimer == null) return;
                var sec = (int)_generationTimer.Elapsed.TotalSeconds;
                progressElapsed.Text = sec < 60
                    ? $"{sec}s"
                    : $"{sec / 60}m {sec % 60:D2}s";
            };
            _elapsedTimer.Start();

            StartShimmer();
        }

        private void UpdateProgress(int completed, int total, string currentCamera)
        {
            double ratio = total > 0 ? (double)completed / total : 0;
            int pct = (int)Math.Round(ratio * 100);
            progressPercent.Text = $"{pct}%";
            progressCount.Text = $"{completed} / {total}";
            progressCurrent.Text = currentCamera;

            double trackWidth = progressTrack.ActualWidth;
            if (trackWidth <= 0) return;

            var anim = new DoubleAnimation
            {
                To = trackWidth * ratio,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            progressFill.BeginAnimation(WidthProperty, anim);
        }

        private void StartShimmer()
        {
            // The shimmer block (~30% of the track) slides repeatedly across the bar
            // so the progress feels live even when no per-camera tick has come in yet.
            progressTrack.SizeChanged -= OnTrackSizeChanged;
            progressTrack.SizeChanged += OnTrackSizeChanged;
            ApplyShimmerSize();

            var trans = new System.Windows.Media.TranslateTransform();
            progressShimmer.RenderTransform = trans;
            ApplyShimmerAnimation(trans);
        }

        private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyShimmerSize();
            if (progressShimmer.RenderTransform is System.Windows.Media.TranslateTransform t)
                ApplyShimmerAnimation(t);
        }

        private void ApplyShimmerSize()
        {
            if (progressTrack.ActualWidth > 0)
                progressShimmer.Width = progressTrack.ActualWidth * 0.3;
        }

        private void ApplyShimmerAnimation(System.Windows.Media.TranslateTransform trans)
        {
            double w = progressTrack.ActualWidth;
            if (w <= 0) return;

            var anim = new DoubleAnimation
            {
                From = -w * 0.3,
                To = w,
                Duration = TimeSpan.FromSeconds(1.6),
                RepeatBehavior = RepeatBehavior.Forever
            };
            trans.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
        }

        private void StopProgress()
        {
            progressPanel.Visibility = Visibility.Collapsed;
            _elapsedTimer?.Stop();
            _elapsedTimer = null;
            _generationTimer?.Stop();
            _generationTimer = null;

            progressTrack.SizeChanged -= OnTrackSizeChanged;
            if (progressShimmer?.RenderTransform is System.Windows.Media.TranslateTransform t)
                t.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            progressFill.BeginAnimation(WidthProperty, null);
            progressFill.Width = 0;
        }
    }

    internal class CameraTreeNode : INotifyPropertyChanged
    {
        private string _name;
        private bool _isChecked;
        private bool _isFolder;
        private Item _cameraItem;
        private List<CameraTreeNode> _children;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }

        public bool IsFolder
        {
            get => _isFolder;
            set { _isFolder = value; OnPropertyChanged(); }
        }

        public Item CameraItem
        {
            get => _cameraItem;
            set { _cameraItem = value; OnPropertyChanged(); }
        }

        public List<CameraTreeNode> Children
        {
            get => _children;
            set { _children = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal class SnapshotPreviewItem
    {
        public string CameraName { get; set; }
        public BitmapImage Thumbnail { get; set; }
        public string ErrorMessage { get; set; }
        public bool HasImage { get; set; }
        public bool HasError { get; set; }
    }
}
