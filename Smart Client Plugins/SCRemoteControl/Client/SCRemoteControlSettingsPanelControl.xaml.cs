using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SCRemoteControl.Server;

namespace SCRemoteControl.Client
{
    public partial class SCRemoteControlSettingsPanelControl : UserControl
    {
        private static readonly SolidColorBrush BrushGreen = Freeze(new SolidColorBrush(Color.FromRgb(0, 255, 0)));
        private static readonly SolidColorBrush BrushGray = Freeze(new SolidColorBrush(Color.FromRgb(128, 128, 128)));
        private static readonly SolidColorBrush BrushRed = Freeze(new SolidColorBrush(Color.FromRgb(255, 68, 68)));

        private readonly ObservableCollection<TokenEntry> _tokens = new ObservableCollection<TokenEntry>();
        private DispatcherTimer _statusTimer;
        private bool _showingValidationError;

        public SCRemoteControlSettingsPanelControl()
        {
            InitializeComponent();

            SCRemoteControlConfig.Load();

            PopulateInterfaces();

            PortBox.Text = SCRemoteControlConfig.Port.ToString();
            PortBox.PreviewTextInput += (s, e) => e.Handled = !char.IsDigit(e.Text, 0);

            UseTlsCheck.IsChecked = SCRemoteControlConfig.UseTls;
            PfxPathBox.Text = SCRemoteControlConfig.PfxPath;
            PfxPasswordBox.Password = SCRemoteControlConfig.PfxPassword;
            TlsPanel.Visibility = SCRemoteControlConfig.UseTls ? Visibility.Visible : Visibility.Collapsed;

            foreach (var t in SCRemoteControlConfig.ApiTokens)
                _tokens.Add(new TokenEntry { Name = t.Name, Value = t.Value });
            TokenList.ItemsSource = _tokens;

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();
            RefreshStatus();
        }

        private void StatusTimer_Tick(object sender, EventArgs e) => RefreshStatus();

        private void PopulateInterfaces()
        {
            InterfaceCombo.Items.Clear();
            InterfaceCombo.Items.Add(new InterfaceItem { Display = "All Interfaces (0.0.0.0)", Address = "0.0.0.0" });

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up))
                {
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                    {
                        InterfaceCombo.Items.Add(new InterfaceItem
                        {
                            Display = $"{addr.Address} ({ni.Name})",
                            Address = addr.Address.ToString()
                        });
                    }
                }
            }
            catch { }

            if (InterfaceCombo.Items.Cast<InterfaceItem>().All(i => i.Address != "127.0.0.1"))
                InterfaceCombo.Items.Add(new InterfaceItem { Display = "127.0.0.1 (Loopback only)", Address = "127.0.0.1" });

            var currentAddr = SCRemoteControlConfig.ListenAddress;
            for (int i = 0; i < InterfaceCombo.Items.Count; i++)
            {
                if (((InterfaceItem)InterfaceCombo.Items[i]).Address == currentAddr)
                {
                    InterfaceCombo.SelectedIndex = i;
                    break;
                }
            }
            if (InterfaceCombo.SelectedIndex < 0)
                InterfaceCombo.SelectedIndex = 0;
        }

        private void RefreshStatus()
        {
            if (_showingValidationError) return;

            var server = RemoteControlServer.Instance;

            if (server.IsListening)
            {
                ServerStatus.Text = "Listening";
                ServerStatus.Foreground = BrushGreen;
                ServerUrl.Text = GetBrowsableUrl() ?? server.ListenUrl;
                ErrorStatus.Text = string.Empty;
                ErrorStatus.Visibility = Visibility.Collapsed;
                OpenSwaggerButton.IsEnabled = true;
            }
            else
            {
                ServerStatus.Text = "Stopped";
                ServerStatus.Foreground = BrushGray;
                ServerUrl.Text = "-";
                OpenSwaggerButton.IsEnabled = false;

                if (!string.IsNullOrEmpty(server.ErrorMessage))
                {
                    ErrorStatus.Text = $"Error: {server.ErrorMessage}";
                    ErrorStatus.Visibility = Visibility.Visible;
                    ServerStatus.Text = "Error";
                    ServerStatus.Foreground = BrushRed;
                }
                else
                {
                    ErrorStatus.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            _showingValidationError = false;

            var error = Validate();
            if (error != null)
            {
                _showingValidationError = true;
                ErrorStatus.Text = error;
                ErrorStatus.Visibility = Visibility.Visible;
                ServerStatus.Text = "Validation Error";
                ServerStatus.Foreground = BrushRed;
                return;
            }

            Save();
            RestartButton.IsEnabled = false;
            RestartSpinner.Visibility = Visibility.Visible;

            Task.Run(() =>
            {
                try
                {
                    RemoteControlServer.Instance.Restart();
                }
                catch (Exception ex)
                {
                    SCRemoteControlDefinition.Log.Error("Restart failed", ex);
                }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _showingValidationError = false;
                        RestartButton.IsEnabled = true;
                        RestartSpinner.Visibility = Visibility.Collapsed;
                        RefreshStatus();
                    }));
                }
            });
        }

        private string Validate()
        {
            if (!int.TryParse(PortBox.Text, out var port) || port < 1024 || port > 65535)
                return "Port must be a number between 1024 and 65535.";

            if (UseTlsCheck.IsChecked == true)
            {
                var pfxPath = PfxPathBox.Text?.Trim();
                if (string.IsNullOrEmpty(pfxPath))
                    return "HTTPS is enabled but no PFX certificate file is selected.";
                if (!File.Exists(pfxPath))
                    return $"PFX certificate file not found: {pfxPath}";
            }

            var validTokens = _tokens.Count(t => !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Value));
            if (validTokens == 0)
                return "At least one API token is required.";

            return null;
        }

        private void OpenSwagger_Click(object sender, RoutedEventArgs e)
        {
            var url = GetBrowsableUrl();
            if (url != null)
            {
                try { System.Diagnostics.Process.Start(url + "/swagger"); }
                catch (Exception ex) { SCRemoteControlDefinition.Log.Error("Failed to open Swagger UI", ex); }
            }
        }

        private static string GetBrowsableUrl()
        {
            var url = RemoteControlServer.Instance.ListenUrl;
            if (string.IsNullOrEmpty(url)) return null;
            // 0.0.0.0 is not browsable - use localhost instead
            return url.Replace("://0.0.0.0:", "://localhost:");
        }

        private void UseTls_Changed(object sender, RoutedEventArgs e)
        {
            if (TlsPanel != null)
                TlsPanel.Visibility = UseTlsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BrowsePfx_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select PFX Certificate",
                Filter = "PFX files (*.pfx)|*.pfx|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
                PfxPathBox.Text = dlg.FileName;
        }

        private void AddToken_Click(object sender, RoutedEventArgs e)
        {
            _tokens.Add(new TokenEntry
            {
                Name = $"token-{_tokens.Count + 1}",
                Value = SCRemoteControlConfig.GenerateToken()
            });
        }

        private void RemoveToken_Click(object sender, RoutedEventArgs e)
        {
            if (_tokens.Count <= 1) return;
            if (sender is Button btn && btn.Tag is TokenEntry token)
                _tokens.Remove(token);
        }

        private void CopyToken_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string value)
            {
                try { Clipboard.SetText(value); }
                catch { }
            }
        }

        public void Save()
        {
            var selectedInterface = InterfaceCombo.SelectedItem as InterfaceItem;
            SCRemoteControlConfig.ListenAddress = selectedInterface?.Address ?? "0.0.0.0";

            if (int.TryParse(PortBox.Text, out var port) && port >= 1024 && port <= 65535)
                SCRemoteControlConfig.Port = port;

            SCRemoteControlConfig.UseTls = UseTlsCheck.IsChecked == true;
            SCRemoteControlConfig.PfxPath = PfxPathBox.Text?.Trim() ?? string.Empty;
            SCRemoteControlConfig.PfxPassword = PfxPasswordBox.Password ?? string.Empty;

            SCRemoteControlConfig.ApiTokens.Clear();
            foreach (var t in _tokens)
            {
                if (!string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Value))
                    SCRemoteControlConfig.ApiTokens.Add(new ApiToken { Name = t.Name, Value = t.Value });
            }

            SCRemoteControlConfig.Save();
        }

        public void Cleanup()
        {
            if (_statusTimer != null)
            {
                _statusTimer.Tick -= StatusTimer_Tick;
                _statusTimer.Stop();
                _statusTimer = null;
            }
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }
    }

    class InterfaceItem
    {
        public string Display { get; set; }
        public string Address { get; set; }
        public override string ToString() => Display;
    }

    class TokenEntry : INotifyPropertyChanged
    {
        private string _name;
        private string _value;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}
