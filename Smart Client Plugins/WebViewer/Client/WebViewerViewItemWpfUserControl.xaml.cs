using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunitySDK;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace WebViewer.Client
{
    public partial class WebViewerViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private static readonly PluginLog _log = new PluginLog("WebViewer");

        private readonly WebViewerViewItemManager _viewItemManager;
        private object _modeChangedReceiver;

        // WebView2 environment is shared across all instances of the plugin in
        // the same process so they share cookies / cache. The user-data folder
        // is fixed per Windows user, mirroring RemoteManager's approach.
        private static CoreWebView2Environment _sharedEnvironment;
        private static readonly object _envLock = new object();
        private static readonly string _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MSCPlugins", "WebViewer", "WebView2Data");

        private WebView2 _webView;
        private bool _certHandlerAttached;
        private bool _authHandlerAttached;
        private bool _basicAuthAttempted;
        private bool _navigationStartedSeen;

        public WebViewerViewItemWpfUserControl(WebViewerViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            _log.Info($"[ViewItem] Init mode={EnvironmentManager.Instance.Mode}");
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));
            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            _log.Info("[ViewItem] Close");
            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }
            DisposeWebView();
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyMode((Mode)message.Data)));
            return null;
        }

        private void ApplyMode(Mode mode)
        {
            _log.Info($"[ViewItem] ApplyMode mode={mode}");
            if (mode == Mode.ClientSetup)
            {
                ShowSetup();
                return;
            }

            // Live or Playback: hide setup form and (re)load the page. Playback
            // currently behaves the same as live - the page is just an embed
            // and has no native concept of timeline scrub.
            setupPanel.Visibility = Visibility.Collapsed;
            liveRoot.Visibility = Visibility.Visible;
            ApplyTitle();
            LoadPage();
        }

        private void ApplyTitle()
        {
            var title = _viewItemManager.Title ?? "";
            bool show = _viewItemManager.ShowTitle && !string.IsNullOrEmpty(title);
            if (show)
            {
                titleText.Text = title;
                titleText.Visibility = Visibility.Visible;
                titleRow.Height = GridLength.Auto;
            }
            else
            {
                titleText.Visibility = Visibility.Collapsed;
                titleRow.Height = new GridLength(0);
            }
        }

        // -------- Setup mode --------

        private void ShowSetup()
        {
            DisposeWebView();
            liveRoot.Visibility = Visibility.Collapsed;
            setupPanel.Visibility = Visibility.Visible;
            setupHint.Visibility = Visibility.Collapsed;

            urlBox.Text = _viewItemManager.Url ?? "";
            titleBox.Text = _viewItemManager.Title ?? "";
            showTitleCheck.IsChecked = _viewItemManager.ShowTitle;
            userBox.Text = _viewItemManager.Username ?? "";
            passBox.Password = _viewItemManager.Password ?? "";
            autoAcceptCertsCheck.IsChecked = _viewItemManager.AutoAcceptCerts;
            autoLoginCheck.IsChecked = _viewItemManager.AutoLogin;
        }

        private void OnSaveClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var url = (urlBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(url) || !LooksLikeUrl(url))
            {
                setupHint.Text = "Please enter a valid URL (starting with http:// or https://).";
                setupHint.Visibility = Visibility.Visible;
                return;
            }

            _viewItemManager.Url = url;
            _viewItemManager.Title = titleBox.Text ?? "";
            _viewItemManager.ShowTitle = showTitleCheck.IsChecked == true;
            _viewItemManager.Username = userBox.Text ?? "";
            _viewItemManager.Password = passBox.Password ?? "";
            _viewItemManager.AutoAcceptCerts = autoAcceptCertsCheck.IsChecked == true;
            _viewItemManager.AutoLogin = autoLoginCheck.IsChecked == true;
            _viewItemManager.Save();

            setupHint.Text = "Saved. Switch to Live to load the page.";
            setupHint.Visibility = Visibility.Visible;
            _log.Info($"[ViewItem] Saved url='{url}' title='{_viewItemManager.Title}' creds={(string.IsNullOrEmpty(_viewItemManager.Username) ? "no" : "yes")}");
        }

        private void OnTestClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var url = (urlBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(url) || !LooksLikeUrl(url))
            {
                setupHint.Text = "Enter a URL first.";
                setupHint.Visibility = Visibility.Visible;
                return;
            }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { _log.Error($"[ViewItem] Test in browser failed: {ex.Message}"); }
        }

        private static bool LooksLikeUrl(string s)
        {
            return Uri.TryCreate(s, UriKind.Absolute, out var u)
                   && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        }

        // -------- Live / playback --------

        private void LoadPage()
        {
            var url = _viewItemManager.Url;
            if (string.IsNullOrEmpty(url) || !LooksLikeUrl(url))
            {
                ShowError("No URL configured. Switch to Setup mode to enter one.");
                return;
            }

            ShowLoading("Loading " + url);

            try
            {
                EnsureWebView();
                EnsureEnvironmentAndNavigateAsync(url);
            }
            catch (Exception ex)
            {
                _log.Error($"[ViewItem] LoadPage failed: {ex.Message}", ex);
                ShowError(ex.Message);
            }
        }

        private void EnsureWebView()
        {
            if (_webView != null) return;
            _webView = new WebView2 { Visibility = Visibility.Visible };
            webHost.Children.Add(_webView);
        }

        private async void EnsureEnvironmentAndNavigateAsync(string url)
        {
            try
            {
                CoreWebView2Environment env;
                lock (_envLock)
                {
                    env = _sharedEnvironment;
                }
                if (env == null)
                {
                    env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
                    lock (_envLock)
                    {
                        if (_sharedEnvironment == null) _sharedEnvironment = env;
                        else env = _sharedEnvironment;
                    }
                }
                if (_webView == null) return;
                await _webView.EnsureCoreWebView2Async(env);
                AttachCoreHandlers();
                _basicAuthAttempted = false;
                _navigationStartedSeen = false;
                _log.Info($"[ViewItem] Navigate url='{url}'");
                _webView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                _log.Error($"[ViewItem] WebView2 init/navigate failed: {ex.Message}", ex);
                ShowError(ex.Message);
            }
        }

        private void AttachCoreHandlers()
        {
            if (_webView?.CoreWebView2 == null) return;

            // Cert tolerance is opt-in per saved config; subscribe only once.
            if (_viewItemManager.AutoAcceptCerts && !_certHandlerAttached)
            {
                _webView.CoreWebView2.ServerCertificateErrorDetected += OnCertificateError;
                _certHandlerAttached = true;
            }

            if (!_authHandlerAttached)
            {
                _webView.CoreWebView2.BasicAuthenticationRequested += OnBasicAuthRequested;
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                _authHandlerAttached = true;
            }
        }

        private void OnCertificateError(object sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
        {
            // Same posture as RemoteManager: when AutoAcceptCerts is on, suppress
            // the cert prompt entirely so embedded dashboards with self-signed
            // certs Just Work.
            e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        }

        private void OnBasicAuthRequested(object sender, CoreWebView2BasicAuthenticationRequestedEventArgs e)
        {
            if (!_viewItemManager.AutoLogin) return;
            if (_basicAuthAttempted) return; // guard against infinite retry on bad creds
            var user = _viewItemManager.Username ?? "";
            var pass = _viewItemManager.Password ?? "";
            if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass)) return;
            _basicAuthAttempted = true;
            e.Response.UserName = user;
            e.Response.Password = pass;
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _navigationStartedSeen = true;
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                loadingPanel.Visibility = Visibility.Collapsed;
                errorPanel.Visibility = Visibility.Collapsed;
                if (_webView != null) _webView.Visibility = Visibility.Visible;
            }
            else
            {
                _log.Info($"[ViewItem] Navigation failed status={e.WebErrorStatus}");
                ShowError($"Navigation failed: {e.WebErrorStatus}");
            }
        }

        private void ShowLoading(string message)
        {
            loadingText.Text = message;
            loadingPanel.Visibility = Visibility.Visible;
            errorPanel.Visibility = Visibility.Collapsed;
            if (_webView != null) _webView.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            errorText.Text = message ?? "";
            errorPanel.Visibility = Visibility.Visible;
            loadingPanel.Visibility = Visibility.Collapsed;
            if (_webView != null) _webView.Visibility = Visibility.Collapsed;
        }

        private void OnReloadClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            LoadPage();
        }

        private void DisposeWebView()
        {
            if (_webView == null) return;
            try
            {
                if (_webView.CoreWebView2 != null)
                {
                    if (_certHandlerAttached)
                        _webView.CoreWebView2.ServerCertificateErrorDetected -= OnCertificateError;
                    if (_authHandlerAttached)
                    {
                        _webView.CoreWebView2.BasicAuthenticationRequested -= OnBasicAuthRequested;
                        _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                        _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                    }
                }
            }
            catch { }
            _certHandlerAttached = false;
            _authHandlerAttached = false;
            try { webHost.Children.Remove(_webView); } catch { }
            try { _webView.Dispose(); } catch { }
            _webView = null;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e) => FireClickEvent();
        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => FireDoubleClickEvent();

        public override bool Maximizable => true;
        public override bool Selectable => true;
        public override bool ShowToolbar => false;
    }
}
