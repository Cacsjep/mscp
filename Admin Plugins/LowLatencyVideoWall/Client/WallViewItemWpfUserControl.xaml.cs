using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LowLatencyVideoWall.Background;
using LowLatencyVideoWall.Config;
using LowLatencyVideoWall.Model;
using LowLatencyVideoWall.Render;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace LowLatencyVideoWall.Client
{
    /// <summary>
    /// The wall surface. Owns one renderer for every pane rather than one per camera, loads
    /// the view assigned to the logged in user, and switches view when a rule says so.
    ///
    /// Almost nothing is drawn here. The hosted swapchain window paints over every WPF
    /// sibling, so pane titles, borders, progress rings, error panels and the diagnostics
    /// overlay are all drawn into the backbuffer by the renderer. What is left in WPF is the
    /// view selector above the wall and the message shown when there is no wall to draw, and
    /// the host is hidden for the latter so the message is not painted over.
    /// </summary>
    public partial class WallViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private WallRenderer _renderer;
        private WallVideoHost _videoHost;

        private List<ViewProfile> _profiles = new List<ViewProfile>();
        private ViewProfile _currentProfile;

        private CancellationTokenSource _loadCts;
        private bool _suppressSelectionEvent;
        private bool _closed;

        private int _wallWidth;
        private int _wallHeight;

        public WallViewItemWpfUserControl()
        {
            InitializeComponent();
        }

        public override bool ShowToolbar => false;
        public override bool Selectable => true;
        public override bool Maximizable => true;

        public override void Init()
        {
            try
            {
                WallClientSettings.Load();
                ApplySettings();
                WallClientSettings.Changed += OnSettingsChanged;

                EnsureRenderer();
                LoadProfileList();

                // A rule may have switched this user's wall before the workspace was opened,
                // so honour the pending request rather than the assigned default.
                var startup = WallSessionBuilder.ResolveStartupProfile(
                    CurrentUserName(), WallClientBackgroundPlugin.PendingViewProfileId);

                WallClientBackgroundPlugin.WallViewRequested += OnWallViewRequested;
                WallClientBackgroundPlugin.WallCamerasRequested += OnWallCamerasRequested;

                // A rule that composed a wall before the workspace opened wins over the
                // assigned default, the same way a rule naming a stored view does.
                var pendingCameras = WallClientBackgroundPlugin.PendingCameras;
                if (pendingCameras != null) ApplyCommand(pendingCameras);
                else if (startup != null) SelectProfile(startup.Id, "assigned view");
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Wall init failed: {ex}");
                ShowMessage($"The wall could not be started: {ex.Message}");
            }
        }

        public override void Close()
        {
            _closed = true;
            WallClientBackgroundPlugin.WallViewRequested -= OnWallViewRequested;
            WallClientBackgroundPlugin.WallCamerasRequested -= OnWallCamerasRequested;
            WallClientSettings.Changed -= OnSettingsChanged;

            CancelLoad();

            _renderer?.Stop();
            _renderer?.Dispose();
            _renderer = null;

            if (_videoHost != null)
            {
                _videoHost.MouseMovedPixel -= OnHostMouseMoved;
                _videoHost.MouseLeftHost -= OnHostMouseLeft;
                _videoHost.DoubleClickedPixel -= OnHostDoubleClick;
                wallArea.Children.Remove(_videoHost);
                _videoHost.Dispose();
                _videoHost = null;
            }
        }

        // ---- setup ----------------------------------------------------------------------

        private void EnsureRenderer()
        {
            if (_renderer != null) return;

            _renderer = new WallRenderer();

            // The video engine takes about two seconds to come up on a cold GPU, so pay that
            // now rather than on the first frame.
            Task.Run(() =>
            {
                try { _renderer.Warmup(); }
                catch (Exception ex) { LowLatencyVideoWallDefinition.Log.Error($"Warmup failed: {ex.Message}"); }
            });

            _videoHost = new WallVideoHost();
            _videoHost.WindowCreated += hwnd => _renderer?.SetWindow(hwnd);
            _videoHost.PixelSizeChanged += OnWallPixelSizeChanged;

            // The hosted window swallows mouse input, so hover and double click have to come
            // back out of it rather than from WPF routed events, which never fire over it.
            _videoHost.MouseMovedPixel += OnHostMouseMoved;
            _videoHost.MouseLeftHost += OnHostMouseLeft;
            _videoHost.DoubleClickedPixel += OnHostDoubleClick;

            wallArea.Children.Insert(0, _videoHost);
            _renderer.Start();
        }

        private void OnWallPixelSizeChanged(int width, int height)
        {
            _renderer?.SetTargetSize(width, height);

            var changed = width != _wallWidth || height != _wallHeight;
            _wallWidth = width;
            _wallHeight = height;

            // Pane sizes drive the resolution asked of each camera, so a real layout change
            // means rebuilding the session. Resizes are debounced by only reacting when the
            // size actually changed.
            if (changed && _currentProfile != null) StartLoad(_currentProfile);
        }

        private void LoadProfileList()
        {
            _profiles = WallConfigStore.LoadViewProfiles();

            _suppressSelectionEvent = true;
            viewSelector.ItemsSource = _profiles;
            viewSelector.DisplayMemberPath = "Name";
            _suppressSelectionEvent = false;

            if (_profiles.Count == 0)
                ShowMessage("No wall views are defined yet. Create one in the Management Client.");
        }

        /// <summary>
        /// Delegated rather than duplicated: LoginNetworkCredential is obsolete and returns
        /// null for OAuth logins, so the background plugin owns the fallback chain through
        /// LoginSettings. One copy of that logic is enough.
        /// </summary>
        private static string CurrentUserName() => WallClientBackgroundPlugin.CurrentUserName();

        // ---- settings -------------------------------------------------------------------

        private void OnSettingsChanged()
        {
            // Raised on whichever thread saved the settings dialog.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                ApplySettings();
            }));
        }

        /// <summary>
        /// Only the view selector lives in WPF. Everything else the settings dialog offers is
        /// read by the renderer on its next pass, so there is nothing to push.
        /// </summary>
        private void ApplySettings()
        {
            selectorBar.Visibility = WallClientSettings.ShowViewSelection
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // ---- view selection -------------------------------------------------------------

        private void SelectProfile(Guid profileId, string reason)
        {
            var profile = _profiles.FirstOrDefault(p => p.Id == profileId)
                          ?? WallConfigStore.LoadViewProfile(profileId);

            if (profile == null)
            {
                ShowMessage("The requested view no longer exists.");
                return;
            }

            if (_profiles.All(p => p.Id != profile.Id))
            {
                _profiles.Add(profile);
                LoadProfileList();
            }

            _suppressSelectionEvent = true;
            viewSelector.SelectedItem = _profiles.FirstOrDefault(p => p.Id == profile.Id);
            _suppressSelectionEvent = false;

            LowLatencyVideoWallDefinition.Log.Info($"Switching wall to '{profile.Name}' ({reason})");
            StartLoad(profile);
        }

        private void OnViewSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvent) return;
            if (viewSelector.SelectedItem is ViewProfile profile) StartLoad(profile);
        }

        private void OnWallViewRequested(WallMessages.SetWallViewPayload payload)
        {
            if (payload == null) return;

            // Arrives on a messaging thread, and everything below touches the UI.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                SelectProfile(payload.ViewProfileId, "rule action");
            }));
        }

        private void OnWallCamerasRequested(WallMessages.SetWallCamerasPayload payload)
        {
            if (payload == null) return;

            // Arrives on a messaging thread, and everything below touches the UI.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                ApplyCommand(payload);
            }));
        }

        /// <summary>
        /// A wall the rule composed. The result is an ordinary view profile that is never
        /// saved, so everything downstream treats it exactly like a stored one, including the
        /// rebuild on resize.
        /// </summary>
        private void ApplyCommand(WallMessages.SetWallCamerasPayload payload)
        {
            LowLatencyVideoWallDefinition.Log.Info($"Wall command: {payload.Describe()}");

            // Nothing on the wall belongs to a stored view any more, so the selector must not
            // keep claiming one is showing.
            _suppressSelectionEvent = true;
            viewSelector.SelectedItem = null;
            _suppressSelectionEvent = false;

            if (payload.Command == WallMessages.WallCommand.Clear)
            {
                CancelLoad();
                _currentProfile = null;
                _renderer?.SetPanes(new List<WallPane>());
                ShowMessage("The wall was cleared by a rule.");
                return;
            }

            var cameras = payload.Cameras
                .Where(c => c != null && c.Id != Guid.Empty)
                .Select(c => new WallLayoutBuilder.CameraRef(c.Id, c.Name))
                .ToList();

            if (cameras.Count == 0)
            {
                ShowMessage("A rule asked for cameras the configuration does not have.");
                return;
            }

            var profile = payload.Command == WallMessages.WallCommand.PlaceAt
                ? WallLayoutBuilder.PlaceAt(_currentProfile, payload.Position, cameras[0])
                : WallLayoutBuilder.Grid(cameras, "Rule");

            StartLoad(profile);
        }

        // ---- loading --------------------------------------------------------------------

        private void StartLoad(ViewProfile profile)
        {
            if (profile == null || _closed) return;

            _currentProfile = profile;

            if (_wallWidth <= 0 || _wallHeight <= 0)
            {
                // The host window has not been measured yet; the size callback will retry.
                return;
            }

            CancelLoad();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            ShowMessage($"Loading '{profile.Name}' ...");

            Task.Run(async () =>
            {
                try
                {
                    var built = await WallSessionBuilder.BuildAsync(profile, token);
                    if (token.IsCancellationRequested) return;

                    await Dispatcher.BeginInvoke(new Action(() => ApplySession(built, profile)));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LowLatencyVideoWallDefinition.Log.Error($"Loading view '{profile.Name}' failed: {ex}");
                    await Dispatcher.BeginInvoke(new Action(() =>
                        ShowMessage($"'{profile.Name}' could not be loaded: {ex.Message}")));
                }
            }, token);
        }

        private void ApplySession(WallSessionBuilder.BuildResult built, ViewProfile profile)
        {
            if (_closed || _renderer == null) return;

            _renderer.SetPanes(built.Panes);

            foreach (var problem in built.Problems)
                LowLatencyVideoWallDefinition.Log.Error($"View '{profile.Name}': {problem}");

            if (built.Panes.Count > 0)
            {
                // A pane that failed for its own reasons says so on the pane itself, drawn by
                // the renderer, so there is nothing left to report up here.
                ShowWall();
                return;
            }

            ShowMessage(built.Problems.Count > 0
                ? $"'{profile.Name}' has no pane that could be started.\n\n{built.Problems[0]}"
                : $"'{profile.Name}' has no cameras. Add some in the Management Client.");
        }

        private void CancelLoad()
        {
            try { _loadCts?.Cancel(); } catch { }
            _loadCts?.Dispose();
            _loadCts = null;
        }

        /// <summary>
        /// Shows a wall level message. The hosted window has to be hidden for it: it is the
        /// topmost thing in this control and would otherwise paint straight over the text.
        /// Hidden rather than collapsed, so the host keeps its size and the swapchain is not
        /// resized to nothing and back.
        /// </summary>
        private void ShowMessage(string text)
        {
            placeholderText.Text = text;
            placeholderText.Visibility = Visibility.Visible;
            if (_videoHost != null) _videoHost.Visibility = Visibility.Hidden;
        }

        private void ShowWall()
        {
            placeholderText.Visibility = Visibility.Collapsed;
            if (_videoHost != null) _videoHost.Visibility = Visibility.Visible;
        }

        // ---- interaction ----------------------------------------------------------------

        private void OnHostMouseMoved(int x, int y) => _renderer?.SetMousePosition(x, y);

        private void OnHostMouseLeft() => _renderer?.ClearMousePosition();

        /// <summary>
        /// Double click toggles one pane filling the wall. The pane rectangles change, so the
        /// maximised camera's stream is restarted at a higher resolution by the renderer's own
        /// resolution adaptation.
        /// </summary>
        private void OnHostDoubleClick(int x, int y)
        {
            if (_renderer == null) return;

            try
            {
                if (_renderer.MaximisedPane != null)
                {
                    _renderer.SetMaximisedPane(null);
                    return;
                }

                var hit = _renderer.HitTest(x, y);
                if (hit != null) _renderer.SetMaximisedPane(hit);
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Maximise failed: {ex.Message}");
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _renderer?.MaximisedPane == null) return;

            try { _renderer.SetMaximisedPane(null); }
            catch (Exception ex) { LowLatencyVideoWallDefinition.Log.Error($"Restore failed: {ex.Message}"); }

            e.Handled = true;
        }
    }
}
