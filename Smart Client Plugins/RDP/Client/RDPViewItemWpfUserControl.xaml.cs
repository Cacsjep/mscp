using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AxMSTSCLib;
using MSTSCLib;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace RDP.Client
{
    public partial class RDPViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private const int ConnectionTimeoutSeconds = 5;

        private readonly RDPViewItemManager _viewItemManager;
        private AxMsRdpClient8NotSafeForScripting _rdpClient;
        private object _modeChangedReceiver;
        private bool _isConnected;
        private Storyboard _spinnerStoryboard;

        public RDPViewItemWpfUserControl(RDPViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));

            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }

            DisconnectAndDispose();
        }

        #region Mode handling

        private void ApplyMode(Mode mode)
        {
            if (mode == Mode.ClientSetup)
            {
                // Setup mode: disconnect if connected, show setup overlay
                if (_isConnected)
                    DisconnectAndDispose();

                _isConnected = false;
                StopSpinner();
                connectingOverlay.Visibility = Visibility.Collapsed;
                connectedToolbar.Visibility = Visibility.Collapsed;
                rdpHost.Visibility = Visibility.Collapsed;
                loginOverlay.Visibility = Visibility.Collapsed;
                setupOverlay.Visibility = Visibility.Visible;

                UpdateSetupInfo();
            }
            else
            {
                // Live mode
                setupOverlay.Visibility = Visibility.Collapsed;

                if (_isConnected)
                {
                    ShowConnectedState();
                }
                else
                {
                    ShowDisconnectedState();
                }
            }
        }

        private void ShowConnectedState()
        {
            StopSpinner();
            connectingOverlay.Visibility = Visibility.Collapsed;
            connectedToolbar.Visibility = Visibility.Visible;
            // Restore full size so the RDP session fills the view
            rdpHost.Width = double.NaN;
            rdpHost.Height = double.NaN;
            rdpHost.Visibility = Visibility.Visible;
            loginOverlay.Visibility = Visibility.Collapsed;
            setupOverlay.Visibility = Visibility.Collapsed;
            UpdateToolbarName();
        }

        private void ShowDisconnectedState()
        {
            StopSpinner();
            connectingOverlay.Visibility = Visibility.Collapsed;
            connectedToolbar.Visibility = Visibility.Collapsed;
            rdpHost.Width = double.NaN;
            rdpHost.Height = double.NaN;
            rdpHost.Visibility = Visibility.Collapsed;
            loginOverlay.Visibility = Visibility.Visible;
            setupOverlay.Visibility = Visibility.Collapsed;

            UpdateLoginInfo();
        }

        private void ShowConnectingState()
        {
            var name = _viewItemManager.ConnectionName;
            connectingNameText.Text = string.IsNullOrWhiteSpace(name) ? "Remote Desktop" : name;
            connectingInfoText.Text = GetConnectionInfo();

            loginOverlay.Visibility = Visibility.Collapsed;
            setupOverlay.Visibility = Visibility.Collapsed;
            connectedToolbar.Visibility = Visibility.Collapsed;

            // WindowsFormsHost uses a separate HWND that renders on top of all WPF content
            // (airspace problem). We can't overlay it. Instead, keep it at 1x1 pixel so the
            // ActiveX control gets a window handle but the user only sees the connecting overlay.
            rdpHost.Width = 1;
            rdpHost.Height = 1;
            rdpHost.Visibility = Visibility.Visible;
            connectingOverlay.Visibility = Visibility.Visible;

            StartSpinner();
        }

        private void StartSpinner()
        {
            StopSpinner();
            var animation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            _spinnerStoryboard = new Storyboard();
            _spinnerStoryboard.Children.Add(animation);
            Storyboard.SetTarget(animation, spinnerArc);
            Storyboard.SetTargetProperty(animation,
                new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
            _spinnerStoryboard.Begin();
        }

        private void StopSpinner()
        {
            if (_spinnerStoryboard != null)
            {
                _spinnerStoryboard.Stop();
                _spinnerStoryboard = null;
            }
        }

        #endregion

        #region RDP Connect / Disconnect

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectRdp();
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            DisconnectAndDispose();
            _isConnected = false;
            ShowDisconnectedState();
            SetStatus("Disconnected", isError: false);
        }

        private void ConnectRdp()
        {
            ClearErrors();

            var server = _viewItemManager.IPAddress;
            if (string.IsNullOrWhiteSpace(server))
            {
                ShowError("IP Address is not configured. Set it in the Properties panel (Setup mode).");
                return;
            }

            DisconnectAndDispose();

            try
            {
                // Show connecting overlay (covers white RDP host)
                ShowConnectingState();
                rdpHost.UpdateLayout();

                // Create a fresh RDP control
                _rdpClient = new AxMsRdpClient8NotSafeForScripting();
                rdpHost.Child = _rdpClient;

                _rdpClient.OnConnected += RdpClient_OnConnected;
                _rdpClient.OnLoginComplete += RdpClient_OnLoginComplete;
                _rdpClient.OnDisconnected += RdpClient_OnDisconnected;
                _rdpClient.OnFatalError += RdpClient_OnFatalError;

                rdpHost.UpdateLayout();

                _rdpClient.Server = server;
                _rdpClient.UserName = _viewItemManager.Username;

                // Use the connecting overlay size (full area) since rdpHost is 1x1 during connect
                int width = (int)connectingOverlay.ActualWidth;
                int height = (int)connectingOverlay.ActualHeight;
                if (width < 200) width = 1024;
                if (height < 200) height = 768;
                _rdpClient.DesktopWidth = width;
                _rdpClient.DesktopHeight = height;
                _rdpClient.ColorDepth = 32;

                // Display
                _rdpClient.AdvancedSettings8.SmartSizing = true;

                // Authentication
                bool enableNla = _viewItemManager.EnableNLA;
                _rdpClient.AdvancedSettings8.EnableCredSspSupport = enableNla;
                _rdpClient.AdvancedSettings8.RDPPort = 3389;
                _rdpClient.AdvancedSettings8.AuthenticationLevel = 2;
                _rdpClient.AdvancedSettings8.NegotiateSecurityLayer = !enableNla;

                // Redirection — all disabled except clipboard if configured
                _rdpClient.AdvancedSettings8.RedirectDrives = false;
                _rdpClient.AdvancedSettings8.RedirectPrinters = false;
                _rdpClient.AdvancedSettings8.RedirectClipboard = _viewItemManager.EnableClipboard;
                _rdpClient.AdvancedSettings8.RedirectSmartCards = false;
                _rdpClient.AdvancedSettings8.RedirectPorts = false;

                // Session hygiene — no local caching of credentials/bitmaps
                _rdpClient.AdvancedSettings8.PublicMode = true;

                // Connection timeout
                _rdpClient.AdvancedSettings8.overallConnectionTimeout = ConnectionTimeoutSeconds;
                _rdpClient.AdvancedSettings8.singleConnectionTimeout = ConnectionTimeoutSeconds;

                // Password — set via NonScriptable interface, then clear from UI
                var password = loginPasswordBox.Password;
                if (!string.IsNullOrEmpty(password))
                {
                    var secured = (IMsTscNonScriptable)_rdpClient.GetOcx();
                    secured.ClearTextPassword = password;
                }
                loginPasswordBox.Clear();

                _rdpClient.Connect();
                _isConnected = true;
            }
            catch (Exception ex)
            {
                ShowError($"Connection failed: {ex.Message}");
                DisconnectAndDispose();
                _isConnected = false;
                ShowDisconnectedState();
            }
        }

        private void DisconnectAndDispose()
        {
            if (_rdpClient != null)
            {
                try
                {
                    if (_rdpClient.Connected != 0)
                        _rdpClient.Disconnect();
                }
                catch { }

                try
                {
                    _rdpClient.OnConnected -= RdpClient_OnConnected;
                    _rdpClient.OnLoginComplete -= RdpClient_OnLoginComplete;
                    _rdpClient.OnDisconnected -= RdpClient_OnDisconnected;
                    _rdpClient.OnFatalError -= RdpClient_OnFatalError;
                }
                catch { }

                try
                {
                    rdpHost.Child = null;
                    _rdpClient.Dispose();
                }
                catch { }

                _rdpClient = null;
            }
        }

        #endregion

        #region RDP Events

        private void RdpClient_OnConnected(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isConnected = true;
                ClearErrors();
                ShowConnectedState();
            }));
        }

        private void RdpClient_OnLoginComplete(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetStatus("Logged in", isError: false);
            }));
        }

        private void RdpClient_OnDisconnected(object sender, IMsTscAxEvents_OnDisconnectedEvent e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int reason = e.discReason;
                DisconnectAndDispose();
                _isConnected = false;
                ShowDisconnectedState();

                if (reason == 1 || reason == 2 || reason == 3)
                {
                    overlayErrorText.Text = string.Empty;
                }
                else
                {
                    var message = GetDisconnectReason(reason);
                    overlayErrorText.Text = $"{message} (code {reason})";
                }
            }));
        }

        private void RdpClient_OnFatalError(object sender, IMsTscAxEvents_OnFatalErrorEvent e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DisconnectAndDispose();
                _isConnected = false;
                ShowDisconnectedState();
                overlayErrorText.Text = $"Fatal error (code {e.errorCode})";
            }));
        }

        private static string GetDisconnectReason(int code)
        {
            switch (code)
            {
                // Basic disconnect reasons
                case 0: return "No information available";
                case 1: return "Local disconnection";
                case 2: return "Remote disconnection by user";
                case 3: return "Remote disconnection by server";

                // Network / socket errors
                case 260: return "DNS name lookup failure";
                case 262: return "Out of memory";
                case 264: return "Connection timed out";
                case 516: return "Could not connect - check IP address and ensure the remote computer is reachable";
                case 518: return "Out of memory";
                case 520: return "Host not found";
                case 772: return "Windows Sockets send failed";
                case 774: return "Out of memory";
                case 776: return "Invalid IP address";
                case 1028: return "Windows Sockets receive failed";
                case 1030: return "Invalid security data";
                case 1032: return "Internal error - invalid computer name";
                case 1286: return "Invalid encryption method";
                case 1288: return "DNS lookup failed";
                case 1540: return "Host name lookup failed";
                case 1542: return "Invalid server security info";
                case 1544: return "Internal timer error";
                case 1796: return "Connection timed out";
                case 1798: return "Failed to unpack server certificate";
                case 2052: return "Bad IP address";
                case 2308: return "Connection lost - network connectivity issue";
                case 2820: return "Connection prevented by an error on the remote computer";

                // Authentication / login errors
                case 2055: return "Login failed - bad username or password";
                case 2056: return "License negotiation failed";
                case 2311: return "Unexpected server authentication certificate received";
                case 2567: return "User account does not exist";
                case 2823: return "Account is disabled";
                case 2825: return "Remote computer requires Network Level Authentication (enable NLA in settings)";
                case 3079: return "Account restricted - a policy is preventing logon";
                case 3335: return "Account locked out - too many logon attempts";
                case 3591: return "Account has expired";
                case 3847: return "Password has expired";
                case 4103: return "Login restricted - outside allowed hours";
                case 4359: return "Login restricted - computer not authorized";
                case 4615: return "Password must be changed before first logon";
                case 4871: return "Logon type (network/interactive) is restricted";

                // Security / encryption errors
                case 2310: return "Internal security error";
                case 2312: return "Licensing timeout";
                case 2566: return "Internal security error";
                case 2822: return "Encryption error";
                case 3078: return "Decryption error";
                case 3080: return "Decompression error";
                case 3337: return "Security policy requires password entry on Windows Security dialog";
                case 3590: return "Client does not support FIPS encryption level";
                case 3592: return "Failed to reconnect - please try again";
                case 3593: return "Remote PC does not support Restricted Administration mode";
                case 3848: return "Credentials cannot be sent to the remote computer";

                // Certificate / CredSSP errors
                case 5127: return "Kerberos User2User sub-protocol is required";
                case 5639: return "Delegation policy does not allow credential delegation";
                case 5895: return "Credential delegation requires mutual authentication";
                case 6151: return "No authentication authority could be contacted";
                case 6919: return "Server certificate is expired or invalid";
                case 7175: return "Incorrect smart card PIN";
                case 7431: return "Time/date difference between client and server is too large";
                case 8455: return "Server requires fresh credentials - saved credentials not accepted";
                case 8711: return "Smart card is blocked";
                case 9479: return "Could not auto-reconnect - please reconnect manually";
                case 9732: return "Client and server versions do not match - update client";

                // Video resources
                case 4104: return "Session disconnected - low video resources on remote computer";

                // Smart card related
                case 266: return "Smart card service is not running";
                case 522: return "Smart card reader not detected";
                case 778: return "No smart card inserted in reader";
                case 1034: return "Smart card subsystem error";
                case 1800: return "Already connected to a console session on this computer";

                default: return "Disconnected";
            }
        }

        #endregion

        #region UI Helpers

        private string GetConnectionInfo()
        {
            var ip = _viewItemManager.IPAddress;
            var user = _viewItemManager.Username;

            if (string.IsNullOrWhiteSpace(ip))
                return "No IP configured";

            return string.IsNullOrWhiteSpace(user) ? ip : $"{user}@{ip}";
        }

        private void UpdateLoginInfo()
        {
            var name = _viewItemManager.ConnectionName;
            loginNameText.Text = string.IsNullOrWhiteSpace(name) ? "Remote Desktop" : name;
            overlayInfoText.Text = GetConnectionInfo();
        }

        private void UpdateSetupInfo()
        {
            var name = _viewItemManager.ConnectionName;
            setupNameText.Text = string.IsNullOrWhiteSpace(name) ? "Remote Desktop" : name;
            setupInfoText.Text = GetConnectionInfo();
        }

        private void UpdateToolbarName()
        {
            var name = _viewItemManager.ConnectionName;
            toolbarNameText.Text = string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        private void SetStatus(string message, bool isError)
        {
            statusText.Text = message;
            statusText.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0xBB, 0xFF));
        }

        private void ShowError(string message)
        {
            overlayErrorText.Text = message;
        }

        private void ClearErrors()
        {
            statusText.Text = string.Empty;
            overlayErrorText.Text = string.Empty;
        }

        #endregion

        #region Smart Client Events

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyMode((Mode)message.Data);
            }));
            return null;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            FireClickEvent();
        }

        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            FireDoubleClickEvent();
        }

        #endregion

        public override bool Maximizable => true;

        public override bool Selectable => true;

        public override bool ShowToolbar => false;
    }
}
