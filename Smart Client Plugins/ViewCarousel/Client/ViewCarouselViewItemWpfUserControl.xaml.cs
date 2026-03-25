using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace ViewCarousel.Client
{
    public partial class ViewCarouselViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly ViewCarouselViewItemManager _viewItemManager;
        private object _modeChangedReceiver;

        private List<CarouselViewEntry> _entries = new List<CarouselViewEntry>();
        private int _defaultTime = 10;
        private int _currentIndex = -1;
        private bool _paused;

        private readonly DispatcherTimer _carouselTimer = new DispatcherTimer();
        private readonly List<ImageViewerWpfControl> _viewers = new List<ImageViewerWpfControl>();
        private readonly List<FrameworkElement> _placeholders = new List<FrameworkElement>();

        // Resolved view data: layout rectangles + camera FQIDs per view
        private List<ResolvedView> _resolvedViews;

        public ViewCarouselViewItemWpfUserControl(ViewCarouselViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
            _carouselTimer.Tick += CarouselTimer_Tick;
        }

        public override void Init()
        {
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));

            LoadConfig();
            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            _carouselTimer.Tick -= CarouselTimer_Tick;
            _carouselTimer.Stop();

            StopCarousel();
            DisposeAllViewers();

            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }
        }

        private void LoadConfig()
        {
            _entries = _viewItemManager.GetViewEntryList();
            if (int.TryParse(_viewItemManager.DefaultTime, out int dt) && dt > 0)
                _defaultTime = dt;
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyMode((Mode)message.Data)));
            return null;
        }

        private void ApplyMode(Mode mode)
        {
            if (mode == Mode.ClientSetup)
            {
                StopCarousel();
                DisposeAllViewers();
                cameraCanvas.Visibility = Visibility.Collapsed;
                playerControls.Visibility = Visibility.Hidden;
                infoText.Visibility = Visibility.Collapsed;
                setupOverlay.Visibility = Visibility.Visible;

                LoadConfig();
                UpdateSetupSummary();

                // Auto-open dialog on first place (no views configured)
                if (_entries.Count == 0)
                {
                    Dispatcher.BeginInvoke(new Action(OpenSetupDialog), DispatcherPriority.Loaded);
                }
            }
            else if (mode == Mode.ClientLive)
            {
                setupOverlay.Visibility = Visibility.Collapsed;
                cameraCanvas.Visibility = Visibility.Visible;

                LoadConfig();

                if (_entries.Count == 0)
                {
                    infoText.Text = "No views configured.\nSwitch to Setup mode to configure the carousel.";
                    infoText.Visibility = Visibility.Visible;
                }
                else
                {
                    infoText.Visibility = Visibility.Collapsed;
                    ResolveViews();
                    StartCarousel();
                }
            }
            else
            {
                // Playback mode
                StopCarousel();
                DisposeAllViewers();
                cameraCanvas.Visibility = Visibility.Collapsed;
                playerControls.Visibility = Visibility.Hidden;
                setupOverlay.Visibility = Visibility.Collapsed;
                infoText.Text = "View Carousel is only active in Live mode.";
                infoText.Visibility = Visibility.Visible;
            }
        }

        private void UpdateSetupSummary()
        {
            if (_entries.Count == 0)
                setupSummary.Text = "Not configured";
            else
                setupSummary.Text = $"{_entries.Count} view(s), {_defaultTime} sec default";
        }

        #region View Resolution

        private void ResolveViews()
        {
            _resolvedViews = new List<ResolvedView>();

            Dictionary<string, ViewAndLayoutItem> viewLookup = null;
            try
            {
                viewLookup = BuildViewLookup();
            }
            catch (Exception ex)
            {
                ViewCarouselDefinition.Log.Error("Failed to resolve views", ex);
                return;
            }

            foreach (var entry in _entries)
            {
                if (viewLookup.TryGetValue(entry.ViewId, out var viewItem))
                {
                    var resolved = new ResolvedView
                    {
                        Name = entry.ViewName,
                        Seconds = entry.CustomTime > 0 ? entry.CustomTime : _defaultTime,
                        Layout = viewItem.Layout,
                        CameraFQIDs = new List<FQID>()
                    };

                    var children = viewItem.GetChildren();
                    if (children != null && resolved.Layout != null)
                    {
                        // Build slot index -> camera FQID mapping
                        var slotMap = new Dictionary<int, FQID>();
                        foreach (var child in children)
                        {
                            if (int.TryParse(child.FQID.ObjectIdString, out int slotIdx)
                                && child.Properties.TryGetValue("CameraId", out string camIdStr)
                                && Guid.TryParse(camIdStr, out Guid camId)
                                && camId != Guid.Empty)
                            {
                                try
                                {
                                    var camItem = Configuration.Instance.GetItem(camId, Kind.Camera);
                                    if (camItem != null)
                                        slotMap[slotIdx] = camItem.FQID;
                                }
                                catch { }
                            }
                        }

                        for (int i = 0; i < resolved.Layout.Length; i++)
                        {
                            resolved.CameraFQIDs.Add(slotMap.ContainsKey(i) ? slotMap[i] : null);
                        }
                    }

                    _resolvedViews.Add(resolved);
                }
                else
                {
                    ViewCarouselDefinition.Log.Info($"View '{entry.ViewName}' ({entry.ViewId}) not found, skipping.");
                }
            }
        }

        private Dictionary<string, ViewAndLayoutItem> BuildViewLookup()
        {
            var lookup = new Dictionary<string, ViewAndLayoutItem>();
            var groups = ClientControl.Instance.GetViewGroupItems();
            if (groups != null)
            {
                foreach (var group in groups)
                    TraverseViews(group, lookup);
            }
            return lookup;
        }

        private void TraverseViews(Item item, Dictionary<string, ViewAndLayoutItem> lookup)
        {
            if (item.FQID.FolderType == FolderType.No && item is ViewAndLayoutItem viewItem)
            {
                lookup[item.FQID.ObjectId.ToString()] = viewItem;
                return;
            }

            if (item is ConfigItem configItem)
            {
                var children = configItem.GetChildren();
                if (children != null)
                {
                    foreach (var child in children)
                        TraverseViews(child, lookup);
                }
            }
        }

        #endregion

        #region Carousel Timer

        private void StartCarousel()
        {
            if (_resolvedViews == null || _resolvedViews.Count == 0) return;

            _paused = false;
            _currentIndex = -1;
            ShowNext();
        }

        private void StopCarousel()
        {
            _carouselTimer.Stop();
            _currentIndex = -1;
        }

        private void CarouselTimer_Tick(object sender, EventArgs e)
        {
            if (_paused) return;
            ShowNext();
        }

        private void ShowNext()
        {
            if (_resolvedViews == null || _resolvedViews.Count == 0) return;

            _currentIndex = (_currentIndex + 1) % _resolvedViews.Count;
            ShowCurrentView();
        }

        private void ShowPrevious()
        {
            if (_resolvedViews == null || _resolvedViews.Count == 0) return;

            _currentIndex = _currentIndex <= 0 ? _resolvedViews.Count - 1 : _currentIndex - 1;
            ShowCurrentView();
        }

        private void ShowCurrentView()
        {
            if (_currentIndex < 0 || _currentIndex >= _resolvedViews.Count) return;

            var view = _resolvedViews[_currentIndex];

            _carouselTimer.Stop();
            _carouselTimer.Interval = TimeSpan.FromSeconds(view.Seconds);

            RenderView(view);

            _carouselTimer.Start();
            UpdateViewInfo();
        }

        private void UpdateViewInfo()
        {
            if (_resolvedViews == null || _currentIndex < 0) return;
            var view = _resolvedViews[_currentIndex];
            lblViewInfo.Text = $"{_currentIndex + 1}/{_resolvedViews.Count}: {view.Name}";
        }

        #endregion

        #region Rendering

        private void RenderView(ResolvedView view)
        {
            if (view.Layout == null || view.Layout.Length == 0) return;

            // Disconnect existing viewers
            foreach (var viewer in _viewers)
            {
                try { viewer.Disconnect(); } catch { }
                viewer.Visibility = Visibility.Collapsed;
            }

            // Hide all placeholders
            foreach (var ph in _placeholders)
                ph.Visibility = Visibility.Collapsed;

            double canvasW = cameraCanvas.ActualWidth;
            double canvasH = cameraCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return;

            int viewerIdx = 0;
            int placeholderIdx = 0;

            for (int i = 0; i < view.Layout.Length; i++)
            {
                var rect = view.Layout[i];

                // Milestone layout uses 0-999 coordinate space
                double x = (rect.X / 999.0) * canvasW;
                double y = (rect.Y / 999.0) * canvasH;
                double w = (rect.Width / 999.0) * canvasW;
                double h = (rect.Height / 999.0) * canvasH;

                bool hasCamera = i < view.CameraFQIDs.Count && view.CameraFQIDs[i] != null;

                if (hasCamera)
                {
                    EnsureViewerCount(viewerIdx + 1);
                    var viewer = _viewers[viewerIdx++];

                    viewer.Width = Math.Max(w, 1);
                    viewer.Height = Math.Max(h, 1);
                    System.Windows.Controls.Canvas.SetLeft(viewer, x);
                    System.Windows.Controls.Canvas.SetTop(viewer, y);
                    viewer.Visibility = Visibility.Visible;

                    viewer.CameraFQID = view.CameraFQIDs[i];
                    viewer.Initialize();
                    viewer.Connect();
                    viewer.ShowImageViewer = true;
                }
                else
                {
                    EnsurePlaceholderCount(placeholderIdx + 1);
                    var ph = _placeholders[placeholderIdx++];

                    ph.Width = Math.Max(w, 1);
                    ph.Height = Math.Max(h, 1);
                    System.Windows.Controls.Canvas.SetLeft(ph, x);
                    System.Windows.Controls.Canvas.SetTop(ph, y);
                    ph.Visibility = Visibility.Visible;
                }
            }
        }

        private void EnsureViewerCount(int count)
        {
            while (_viewers.Count < count)
            {
                var viewer = new ImageViewerWpfControl
                {
                    EnableBrowseMode = false,
                    EnableSetupMode = false,
                    SuppressUpdateOnMotionOnly = true,
                    EnableMouseControlledPtz = false
                };
                _viewers.Add(viewer);
                cameraCanvas.Children.Add(viewer);
            }
        }

        private void EnsurePlaceholderCount(int count)
        {
            while (_placeholders.Count < count)
            {
                var panel = new VideoOS.Platform.UI.Controls.VideoOSPanel
                {
                    BackgroundAppearance = VideoOS.Platform.UI.Controls.VideoOSPanel.BackgroundAppearances.EmptyState,
                    IsBorderVisible = true
                };

                _placeholders.Add(panel);
                cameraCanvas.Children.Add(panel);
            }
        }

        private void DisposeAllViewers()
        {
            foreach (var viewer in _viewers)
            {
                try { viewer.Disconnect(); } catch { }
                try { viewer.Close(); } catch { }
                try { viewer.Dispose(); } catch { }
            }
            _viewers.Clear();

            _placeholders.Clear();

            cameraCanvas.Children.Clear();
        }

        private void CameraCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Re-layout current view if running
            if (_resolvedViews != null && _currentIndex >= 0 && _currentIndex < _resolvedViews.Count)
            {
                RenderView(_resolvedViews[_currentIndex]);
            }
        }

        #endregion

        #region UI Event Handlers

        private void OnSetupClick(object sender, RoutedEventArgs e)
        {
            OpenSetupDialog();
        }

        private void OpenSetupDialog()
        {
            var dialog = new ViewCarouselSetupWindow(_entries, _defaultTime);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                _entries = dialog.ResultEntries;
                _defaultTime = dialog.ResultDefaultTime;
                _viewItemManager.SetViewEntryList(_entries);
                _viewItemManager.DefaultTime = _defaultTime.ToString();
                _viewItemManager.Save();
                UpdateSetupSummary();
            }
        }

        private void OnPrevClick(object sender, RoutedEventArgs e)
        {
            if (_resolvedViews != null && _resolvedViews.Count > 1)
                ShowPrevious();
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            if (_resolvedViews != null && _resolvedViews.Count > 1)
                ShowNext();
        }

        private void OnPauseClick(object sender, RoutedEventArgs e)
        {
            if (_paused)
            {
                _paused = false;
                btnPause.Content = "\u23F8";
                _carouselTimer.Start();
            }
            else
            {
                _paused = true;
                btnPause.Content = "\u25B6";
                _carouselTimer.Stop();
            }
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (EnvironmentManager.Instance.Mode == Mode.ClientLive && _resolvedViews != null && _resolvedViews.Count > 0)
                playerControls.Visibility = Visibility.Visible;
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            playerControls.Visibility = Visibility.Hidden;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e) => FireClickEvent();
        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => FireDoubleClickEvent();

        #endregion

        public override bool Maximizable => true;
        public override bool Selectable => true;
        public override bool ShowToolbar => false;
    }

    internal class ResolvedView
    {
        public string Name { get; set; }
        public int Seconds { get; set; }
        public Rectangle[] Layout { get; set; }
        public List<FQID> CameraFQIDs { get; set; }
    }
}
