using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

        // Live slot UI - rebuilt on every carousel tick. Cameras and plugin user
        // controls need explicit teardown beyond removing them from the canvas, so
        // they're tracked separately. Placeholders just go through Children.Clear.
        private readonly List<ImageViewerWpfControl> _viewers = new List<ImageViewerWpfControl>();
        private readonly List<ViewItemWpfUserControl> _pluginControls = new List<ViewItemWpfUserControl>();

        // Resolved view data for each entry
        private List<ResolvedView> _resolvedViews;

        // Cache of installed third-party ViewItemPlugins keyed on plugin Id
        private Dictionary<Guid, ViewItemPlugin> _viewItemPluginsById;

        // Plugin Ids that have thrown during Init - avoid retrying them every tick.
        private readonly HashSet<Guid> _failedPluginIds = new HashSet<Guid>();

        // Re-entrancy guard: WPF SizeChanged can fire mid-render when child controls
        // alter their own measure pass and we don't want a render -> SizeChanged ->
        // render feedback loop.
        private bool _rendering;

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
            DisposeAll();

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
                DisposeAll();
                slotCanvas.Visibility = Visibility.Collapsed;
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
                slotCanvas.Visibility = Visibility.Visible;

                LoadConfig();

                if (_entries.Count == 0)
                {
                    infoText.Text = "No views configured.\nSwitch to Setup mode to configure the carousel.";
                    infoText.Visibility = Visibility.Visible;
                }
                else
                {
                    infoText.Visibility = Visibility.Collapsed;
                    BuildViewItemPluginCache();
                    ResolveViews();
                    StartCarousel();
                }
            }
            else
            {
                // Playback mode
                StopCarousel();
                DisposeAll();
                slotCanvas.Visibility = Visibility.Collapsed;
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

        private void BuildViewItemPluginCache()
        {
            var lookup = new Dictionary<Guid, ViewItemPlugin>();
            try
            {
                var defs = EnvironmentManager.Instance.AllPluginDefinitions;
                if (defs != null)
                {
                    foreach (var def in defs)
                    {
                        if (def.ViewItemPlugins == null) continue;
                        foreach (var vp in def.ViewItemPlugins)
                        {
                            if (vp == null) continue;
                            // Don't render the carousel inside itself - infinite recursion.
                            if (vp.Id == new Guid("C4446629-E01B-402F-8C8E-2C3235BCD0E3")) continue;
                            lookup[vp.Id] = vp;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewCarouselDefinition.Log.Error("Failed to enumerate ViewItemPlugins", ex);
            }
            _viewItemPluginsById = lookup;
        }

        private void ResolveViews()
        {
            _resolvedViews = new List<ResolvedView>();

            Dictionary<string, ViewAndLayoutItem> viewLookup;
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
                if (!viewLookup.TryGetValue(entry.ViewId, out var viewItem))
                {
                    ViewCarouselDefinition.Log.Info($"View '{entry.ViewName}' ({entry.ViewId}) not found, skipping.");
                    continue;
                }

                if (viewItem.Layout == null || viewItem.Layout.Length == 0)
                    continue;

                var resolved = new ResolvedView
                {
                    Name = entry.ViewName,
                    Seconds = entry.CustomTime > 0 ? entry.CustomTime : _defaultTime,
                    Layout = viewItem.Layout,
                    Slots = BuildSlots(viewItem)
                };

                _resolvedViews.Add(resolved);
            }
        }

        private List<ResolvedSlot> BuildSlots(ViewAndLayoutItem viewItem)
        {
            var slots = new List<ResolvedSlot>();

            var children = viewItem.GetChildren();
            if (children == null) return slots;

            var emptyId = ViewAndLayoutItem.EmptyBuiltinId;
            var cameraId = ViewAndLayoutItem.CameraBuiltinId;

            foreach (var child in children)
            {
                if (child?.Properties == null) continue;

                int slotIdx;
                if (!child.Properties.TryGetValue("Index", out var idxStr)
                    || !int.TryParse(idxStr, out slotIdx))
                {
                    if (!int.TryParse(child.FQID.ObjectIdString, out slotIdx)) continue;
                }

                if (!child.Properties.TryGetValue("ViewItemId", out var vidStr)
                    || !Guid.TryParse(vidStr, out var viewItemId))
                    continue;

                // Empty slot - render as a clean empty pane.
                if (viewItemId == emptyId)
                {
                    slots.Add(new ResolvedSlot { Index = slotIdx, Kind = SlotKind.Empty });
                    continue;
                }

                if (viewItemId == cameraId)
                {
                    if (!child.Properties.TryGetValue("CameraId", out var camIdStr)
                        || !Guid.TryParse(camIdStr, out var camId)
                        || camId == Guid.Empty)
                    {
                        slots.Add(new ResolvedSlot { Index = slotIdx, Kind = SlotKind.Empty });
                        continue;
                    }
                    try
                    {
                        var camItem = Configuration.Instance.GetItem(camId, Kind.Camera);
                        if (camItem != null)
                            slots.Add(new ResolvedSlot { Index = slotIdx, Kind = SlotKind.Camera, CameraFQID = camItem.FQID });
                    }
                    catch (Exception ex)
                    {
                        ViewCarouselDefinition.Log.Info($"Camera slot {slotIdx} resolve failed: {ex.Message}");
                    }
                    continue;
                }

                // Plugin item: look up the ViewItemPlugin and snapshot its properties.
                // Pass through Index/Builtin since some bundled plugins (e.g. Alarm
                // Preview) inspect them in PropertiesLoaded; only ViewItemId is dropped
                // since it's the type marker, not state.
                if (_viewItemPluginsById != null && _viewItemPluginsById.TryGetValue(viewItemId, out var plugin))
                {
                    var props = new Dictionary<string, string>();
                    foreach (var kv in child.Properties)
                    {
                        if (kv.Key == "ViewItemId") continue;
                        props[kv.Key] = kv.Value;
                    }
                    slots.Add(new ResolvedSlot
                    {
                        Index = slotIdx,
                        Kind = SlotKind.Plugin,
                        Plugin = plugin,
                        Properties = props
                    });
                    continue;
                }

                // Built-in non-camera (HotSpot, Map, GisMap, Carousel, Matrix, HTML,
                // Image, Text, SystemMonitor) cannot be standalone-instantiated without
                // driving the workroom selection state. Show a labelled placeholder.
                slots.Add(new ResolvedSlot
                {
                    Index = slotIdx,
                    Kind = SlotKind.Unsupported,
                    UnsupportedLabel = BuiltInName(viewItemId)
                });
            }

            return slots;
        }

        // Image / Text builtin ids are deprecated in 2026 R1 but still appear in saved
        // views from older versions, so match by literal guid to avoid pulling in the
        // obsolete SDK constants.
        private static readonly Guid LegacyImageBuiltInId = new Guid("3a8a7345-1d80-43eb-b556-8982bd3fc64e");
        private static readonly Guid LegacyTextBuiltInId = new Guid("769457d3-1f67-4c61-8529-5a428689a7cf");

        private static string BuiltInName(Guid viewItemId)
        {
            if (viewItemId == ViewAndLayoutItem.HotspotBuiltinId) return "Hotspot";
            if (viewItemId == ViewAndLayoutItem.CarrouselBuiltinId) return "Carousel";
            if (viewItemId == ViewAndLayoutItem.MatrixBuiltinId) return "Matrix";
            if (viewItemId == ViewAndLayoutItem.HTMLBuiltinId) return "HTML page";
            if (viewItemId == ViewAndLayoutItem.MapBuiltinId) return "Map";
            if (viewItemId == ViewAndLayoutItem.GisMapBuiltinId) return "Smart Map";
            if (viewItemId == ViewAndLayoutItem.SystemMonitorBuiltinId) return "System monitor";
            if (viewItemId == ViewAndLayoutItem.ImageTextBuiltInId) return "Image and text";
            if (viewItemId == LegacyImageBuiltInId) return "Image";
            if (viewItemId == LegacyTextBuiltInId) return "Text";
            return "Unknown view item";
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
            if (_rendering) return;
            _rendering = true;
            try
            {
                RenderViewCore(view);
            }
            finally
            {
                _rendering = false;
            }
        }

        private void RenderViewCore(ResolvedView view)
        {
            // Tear down previous tick's UI completely - cheap enough at carousel cadence
            // and avoids cross-talk between view items configured in different slots.
            DisposeAll();

            if (view.Layout == null || view.Layout.Length == 0) return;

            double canvasW = slotCanvas.ActualWidth;
            double canvasH = slotCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return;

            var slotsByIndex = view.Slots.ToDictionary(s => s.Index, s => s);

            for (int i = 0; i < view.Layout.Length; i++)
            {
                var rect = view.Layout[i];

                // Milestone layout uses a 0-999 coordinate space.
                double x = (rect.X / 999.0) * canvasW;
                double y = (rect.Y / 999.0) * canvasH;
                double w = Math.Max((rect.Width / 999.0) * canvasW, 1);
                double h = Math.Max((rect.Height / 999.0) * canvasH, 1);

                if (slotsByIndex.TryGetValue(i, out var slot))
                {
                    switch (slot.Kind)
                    {
                        case SlotKind.Camera:
                            AddCamera(slot, x, y, w, h);
                            continue;
                        case SlotKind.Plugin:
                            if (slot.Plugin != null && _failedPluginIds.Contains(slot.Plugin.Id))
                            {
                                AddUnsupported(slot.Plugin.Name ?? "Plugin", x, y, w, h);
                                continue;
                            }
                            if (TryAddPlugin(slot, x, y, w, h)) continue;
                            if (slot.Plugin != null) _failedPluginIds.Add(slot.Plugin.Id);
                            AddUnsupported(slot.Plugin?.Name ?? "Plugin", x, y, w, h);
                            continue;
                        case SlotKind.Unsupported:
                            AddUnsupported(slot.UnsupportedLabel ?? "View item", x, y, w, h);
                            continue;
                        case SlotKind.Empty:
                            AddPlaceholder(x, y, w, h);
                            continue;
                    }
                }

                AddPlaceholder(x, y, w, h);
            }
        }

        private void AddCamera(ResolvedSlot slot, double x, double y, double w, double h)
        {
            try
            {
                var viewer = new ImageViewerWpfControl
                {
                    EnableBrowseMode = false,
                    EnableSetupMode = false,
                    SuppressUpdateOnMotionOnly = true,
                    EnableMouseControlledPtz = false
                };
                var border = WrapInBorder(viewer, w, h);
                System.Windows.Controls.Canvas.SetLeft(border, x);
                System.Windows.Controls.Canvas.SetTop(border, y);

                _viewers.Add(viewer);
                slotCanvas.Children.Add(border);

                viewer.CameraFQID = slot.CameraFQID;
                viewer.Initialize();
                viewer.Connect();
                viewer.ShowImageViewer = true;
            }
            catch (Exception ex)
            {
                ViewCarouselDefinition.Log.Info($"Camera slot {slot.Index} render failed: {ex.Message}");
                AddPlaceholder(x, y, w, h);
            }
        }

        private bool TryAddPlugin(ResolvedSlot slot, double x, double y, double w, double h)
        {
            ViewItemWpfUserControl ctrl = null;
            try
            {
                var manager = slot.Plugin.GenerateViewItemManager();
                if (manager == null) return false;

                // Hydrate saved state. We deliberately do NOT pre-set manager.FQID -
                // some bundled plugins use FQID identity to register receivers and a
                // pre-set value collides with their internal expectations.
                if (slot.Properties != null)
                {
                    foreach (var kv in slot.Properties)
                    {
                        try { manager.SetProperty(kv.Key, kv.Value); } catch { }
                    }
                }

                try { manager.PropertiesLoaded(); } catch { }

                ctrl = manager.GenerateViewItemWpfUserControl();
                if (ctrl == null) return false;

                var border = WrapInBorder(ctrl, w, h);
                System.Windows.Controls.Canvas.SetLeft(border, x);
                System.Windows.Controls.Canvas.SetTop(border, y);

                slotCanvas.Children.Add(border);
                ctrl.Init();

                _pluginControls.Add(ctrl);
                return true;
            }
            catch (Exception ex)
            {
                ViewCarouselDefinition.Log.Info($"Plugin slot {slot.Index} (plugin {slot.Plugin?.Name}) render failed: {ex.Message}");
                if (ctrl != null)
                {
                    try { ctrl.Close(); } catch { }
                }
                return false;
            }
        }

        // Plain WPF Border around camera and plugin tiles - just paints a 1px
        // separator without any VideoOSPanel internal behavior interference.
        private static readonly System.Windows.Media.SolidColorBrush SlotBorderBrush
            = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x40, 0x40, 0x40));

        private static System.Windows.Controls.Border WrapInBorder(
            System.Windows.UIElement content, double w, double h)
        {
            return new System.Windows.Controls.Border
            {
                BorderBrush = SlotBorderBrush,
                BorderThickness = new System.Windows.Thickness(1),
                Width = w,
                Height = h,
                Child = content
            };
        }

        private void AddPlaceholder(double x, double y, double w, double h)
        {
            // Construction matches the original carousel exactly: no Content (which
            // would override whatever VideoOSPanel paints natively for EmptyState),
            // and size set after construction.
            var panel = new VideoOS.Platform.UI.Controls.VideoOSPanel
            {
                BackgroundAppearance = VideoOS.Platform.UI.Controls.VideoOSPanel.BackgroundAppearances.EmptyState,
                IsBorderVisible = true
            };
            panel.Width = Math.Max(w, 1);
            panel.Height = Math.Max(h, 1);
            System.Windows.Controls.Canvas.SetLeft(panel, x);
            System.Windows.Controls.Canvas.SetTop(panel, y);

            slotCanvas.Children.Add(panel);
        }

        private void AddUnsupported(string itemName, double x, double y, double w, double h)
        {
            var panel = new VideoOS.Platform.UI.Controls.VideoOSPanel
            {
                BackgroundAppearance = VideoOS.Platform.UI.Controls.VideoOSPanel.BackgroundAppearances.EmptyState,
                IsBorderVisible = true,
                Width = w,
                Height = h
            };

            var label = new System.Windows.Controls.TextBlock
            {
                Text = $"{itemName}\nnot supported in carousel",
                Foreground = System.Windows.Media.Brushes.Gray,
                TextAlignment = System.Windows.TextAlignment.Center,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                FontSize = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(8)
            };
            panel.Content = label;

            System.Windows.Controls.Canvas.SetLeft(panel, x);
            System.Windows.Controls.Canvas.SetTop(panel, y);

            slotCanvas.Children.Add(panel);
        }

        private void DisposeAll()
        {
            foreach (var viewer in _viewers)
            {
                try { viewer.Disconnect(); } catch { }
                try { viewer.Close(); } catch { }
                try { viewer.Dispose(); } catch { }
            }
            _viewers.Clear();

            foreach (var ctrl in _pluginControls)
            {
                try { ctrl.Close(); } catch { }
            }
            _pluginControls.Clear();

            slotCanvas.Children.Clear();
        }

        private void SlotCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
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
                btnPause.Content = "⏸";
                _carouselTimer.Start();
            }
            else
            {
                _paused = true;
                btnPause.Content = "▶";
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

    internal enum SlotKind { Camera, Plugin, Empty, Unsupported }

    internal class ResolvedSlot
    {
        public int Index { get; set; }
        public SlotKind Kind { get; set; }
        public FQID CameraFQID { get; set; }
        public ViewItemPlugin Plugin { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public string UnsupportedLabel { get; set; }
    }

    internal class ResolvedView
    {
        public string Name { get; set; }
        public int Seconds { get; set; }
        public Rectangle[] Layout { get; set; }
        public List<ResolvedSlot> Slots { get; set; }
    }

}
