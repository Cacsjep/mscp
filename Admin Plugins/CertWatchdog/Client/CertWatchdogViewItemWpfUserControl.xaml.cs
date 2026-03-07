using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using CertWatchdog.Messaging;
using CertWatchdog.Models;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace CertWatchdog.Client
{
    public partial class CertWatchdogViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private static readonly PluginLog _log = new PluginLog("CertWatchdog");
        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);
        private Guid _pendingRequestId;
        private Timer _refreshTimer;
        private Timer _retryTimer;
        private volatile bool _closing;
        private volatile bool _dataReceived;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(60);

        public CertWatchdogViewItemWpfUserControl()
        {
            InitializeComponent();
        }

        public override void Init()
        {
            // Start message communication on background thread
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _cmh.Start();
                    _cmh.Register(OnCertDataResponse, new CommunicationIdFilter(CertMessageIds.CertDataResponse));

                    // Request cert data now and every 60 minutes
                    RequestCertData();
                    _refreshTimer = new Timer(_ => RequestCertData(), null, RefreshInterval, RefreshInterval);

                    // Retry every 5s until first response arrives
                    // (Event Server may not have its MC filter registered yet)
                    _retryTimer = new Timer(_ =>
                    {
                        if (_dataReceived || _closing)
                        {
                            _retryTimer?.Dispose();
                            _retryTimer = null;
                            return;
                        }
                        RequestCertData();
                    }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        loadingText.Text = $"Failed to connect: {ex.Message}";
                    }));
                }
            });
        }

        public override void Close()
        {
            _closing = true;

            _retryTimer?.Dispose();
            _retryTimer = null;
            _refreshTimer?.Dispose();
            _refreshTimer = null;

            _cmh.Close();
        }

        private void RequestCertData()
        {
            if (_closing || _cmh.MessageCommunication == null) return;

            try
            {
                _pendingRequestId = Guid.NewGuid();
                var request = new CertDataRequest { RequestId = _pendingRequestId };
                _cmh.TransmitMessage(new Message(CertMessageIds.CertDataRequest, request));

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    loadingText.Text = "Awaiting certificate data...";
                    loadingOverlay.Visibility = Visibility.Visible;
                }));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    loadingText.Text = $"Request failed: {ex.Message}";
                }));
            }
        }

        private object OnCertDataResponse(Message message, FQID dest, FQID source)
        {
            if (_closing) return null;
            var response = message.Data as CertDataResponse;
            if (response == null) return null;

            _dataReceived = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                DisplayCertificates(response.Certificates, response.Timestamp);
            }));

            return null;
        }

        private void DisplayCertificates(List<CertificateInfo> certificates, DateTime timestamp)
        {
            if (certificates == null || certificates.Count == 0)
            {
                loadingText.Text = "No certificate data available.\nThe Event Server may still be performing the initial check.";
                loadingOverlay.Visibility = Visibility.Visible;
                statusText.Text = "No endpoints found";
                lastUpdatedText.Text = "";
                return;
            }

            var serverCerts = certificates.Where(c => c.SourceItemId == null).ToList();
            var hwCerts = certificates.Where(c => c.SourceItemId != null).ToList();

            serverDataGrid.ItemsSource = serverCerts;
            hwDataGrid.ItemsSource = hwCerts;
            hwCountText.Text = $"({hwCerts.Count})";
            loadingOverlay.Visibility = Visibility.Collapsed;

            var okCount = certificates.Count(c => c.Status == CertStatus.OK);
            var expiringCount = certificates.Count(c => c.Status == CertStatus.Expiring);
            var criticalCount = certificates.Count(c =>
                c.Status == CertStatus.Critical || c.Status == CertStatus.Expired);
            var errorCount = certificates.Count(c => c.Status == CertStatus.Error);

            statusText.Text = $"{serverCerts.Count} server  |  {hwCerts.Count} hardware  |  " +
                              $"{okCount} OK  |  {expiringCount} Expiring  |  " +
                              $"{criticalCount} Critical  |  {errorCount} Error";

            lastUpdatedText.Text = $"Last checked: {timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            RequestCertData();
        }

        private void OnRecollectClick(object sender, RoutedEventArgs e)
        {
            if (_closing || _cmh.MessageCommunication == null) return;

            try
            {
                _cmh.TransmitMessage(new Message(CertMessageIds.CertRecollectRequest, null));

                loadingText.Text = "Recollecting certificates...";
                loadingOverlay.Visibility = Visibility.Visible;

                // Request updated data after a short delay to allow the check to complete
                _dataReceived = false;
                _retryTimer?.Dispose();
                _retryTimer = new Timer(_ =>
                {
                    if (_dataReceived || _closing)
                    {
                        _retryTimer?.Dispose();
                        _retryTimer = null;
                        return;
                    }
                    RequestCertData();
                }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                loadingText.Text = $"Recollect failed: {ex.Message}";
            }
        }
    }
}
