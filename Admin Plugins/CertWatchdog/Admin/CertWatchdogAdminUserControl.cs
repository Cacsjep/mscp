using System.Drawing;
using System.Windows.Forms;
using VideoOS.Platform;

namespace CertWatchdog.Admin
{
    public class CertWatchdogAdminUserControl : UserControl
    {
        private Label _titleLabel;
        private Label _descriptionLabel;
        private Label _statusLabel;
        private Label _intervalLabel;

        public CertWatchdogAdminUserControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            _titleLabel = new Label
            {
                Text = "Certificate Watchdog",
                Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
                Location = new Point(20, 20),
                AutoSize = true
            };

            _descriptionLabel = new Label
            {
                Text = "Monitors SSL/TLS certificate expiry for all XProtect HTTPS endpoints.\n\n" +
                       "The plugin automatically discovers Management Server, Recording Server,\n" +
                       "and registered service endpoints, then checks their certificates periodically.\n\n" +
                       "Events are fired at 60, 30, and 15 day thresholds before expiry.\n" +
                       "These events can be used in XProtect Rules to trigger alarms or notifications.\n\n" +
                       "View certificate details in the Smart Client 'Certificates' workspace tab.",
                Location = new Point(20, 55),
                AutoSize = true
            };

            _statusLabel = new Label
            {
                Text = "",
                Location = new Point(20, 210),
                AutoSize = true,
                ForeColor = Color.DarkGreen
            };

            _intervalLabel = new Label
            {
                Text = "",
                Location = new Point(20, 235),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            Controls.Add(_titleLabel);
            Controls.Add(_descriptionLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_intervalLabel);

            Name = "CertWatchdogAdminUserControl";
            Size = new Size(600, 300);
            ResumeLayout(false);
            PerformLayout();
        }

        public void FillContent(Item item)
        {
            if (item == null) return;

            _statusLabel.Text = "Status: Active";

            var interval = "6";
            if (item.Properties.ContainsKey("CheckIntervalHours"))
                interval = item.Properties["CheckIntervalHours"];

            _intervalLabel.Text = $"Check interval: {interval} hours";
        }

        public void ClearContent()
        {
            _statusLabel.Text = "";
            _intervalLabel.Text = "";
        }
    }
}
