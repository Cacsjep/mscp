using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using CertWatchdog.Messaging;
using CertWatchdog.Models;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace CertWatchdog.Client
{
    public partial class CertWatchdogViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private MessageCommunication _mc;
        private object _responseFilter;
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
                    var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                    MessageCommunicationManager.Start(serverId);
                    _mc = MessageCommunicationManager.Get(serverId);

                    _responseFilter = _mc.RegisterCommunicationFilter(
                        OnCertDataResponse,
                        new CommunicationIdFilter(CertMessageIds.CertDataResponse));

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

            if (_mc != null && _responseFilter != null)
            {
                _mc.UnRegisterCommunicationFilter(_responseFilter);
                _responseFilter = null;
            }
            _mc = null;
        }

        private void RequestCertData()
        {
            var mc = _mc;
            if (_closing || mc == null) return;

            try
            {
                _pendingRequestId = Guid.NewGuid();
                var request = new CertDataRequest { RequestId = _pendingRequestId };
                mc.TransmitMessage(
                    new Message(CertMessageIds.CertDataRequest, request), null, null, null);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    loadingText.Text = "Requesting certificate data...";
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

            certDataGrid.ItemsSource = certificates;
            loadingOverlay.Visibility = Visibility.Collapsed;

            var okCount = certificates.Count(c => c.Status == CertStatus.OK);
            var expiringCount = certificates.Count(c => c.Status == CertStatus.Expiring);
            var criticalCount = certificates.Count(c =>
                c.Status == CertStatus.Critical || c.Status == CertStatus.Expired);
            var errorCount = certificates.Count(c => c.Status == CertStatus.Error);

            statusText.Text = $"{certificates.Count} endpoint(s)  |  " +
                              $"{okCount} OK  |  {expiringCount} Expiring  |  " +
                              $"{criticalCount} Critical  |  {errorCount} Error";

            lastUpdatedText.Text = $"Last checked: {timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            RequestCertData();
        }
    }
}
