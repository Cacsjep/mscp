namespace RtmpStreamer.Admin
{
    partial class StreamConfigUserControl
    {
        private System.ComponentModel.IContainer components = null;

        private void InitializeComponent()
        {
            this._labelTitle = new System.Windows.Forms.Label();
            this._labelName = new System.Windows.Forms.Label();
            this._txtName = new System.Windows.Forms.TextBox();
            this._labelCamera = new System.Windows.Forms.Label();
            this._btnSelectCamera = new System.Windows.Forms.Button();
            this._labelRtmpUrl = new System.Windows.Forms.Label();
            this._txtRtmpUrl = new System.Windows.Forms.TextBox();
            this._lblRtmpExamples = new System.Windows.Forms.Label();
            this._chkEnabled = new System.Windows.Forms.CheckBox();
            this._chkAllowUntrustedCerts = new System.Windows.Forms.CheckBox();
            this._grpLiveStatus = new System.Windows.Forms.GroupBox();
            this._lblStatusLabel = new System.Windows.Forms.Label();
            this._lblStatusValue = new System.Windows.Forms.Label();
            this._lblStatsValue = new System.Windows.Forms.Label();
            this._dgvLog = new System.Windows.Forms.DataGridView();
            this._colTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this._colLevel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this._colMessage = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this._grpLiveStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._dgvLog)).BeginInit();
            this.SuspendLayout();
            //
            // _labelTitle
            //
            this._labelTitle.AutoSize = true;
            this._labelTitle.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this._labelTitle.Location = new System.Drawing.Point(12, 12);
            this._labelTitle.Name = "_labelTitle";
            this._labelTitle.Size = new System.Drawing.Size(223, 21);
            this._labelTitle.TabIndex = 0;
            this._labelTitle.Text = "RTMP Stream Configuration";
            //
            // _labelName
            //
            this._labelName.AutoSize = true;
            this._labelName.Location = new System.Drawing.Point(14, 50);
            this._labelName.Name = "_labelName";
            this._labelName.Size = new System.Drawing.Size(38, 13);
            this._labelName.TabIndex = 1;
            this._labelName.Text = "Name:";
            //
            // _txtName
            //
            this._txtName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._txtName.Location = new System.Drawing.Point(110, 47);
            this._txtName.Name = "_txtName";
            this._txtName.Size = new System.Drawing.Size(400, 20);
            this._txtName.TabIndex = 0;
            this._txtName.TextChanged += new System.EventHandler(this.OnUserChange);
            //
            // _labelCamera
            //
            this._labelCamera.AutoSize = true;
            this._labelCamera.Location = new System.Drawing.Point(14, 80);
            this._labelCamera.Name = "_labelCamera";
            this._labelCamera.Size = new System.Drawing.Size(46, 13);
            this._labelCamera.TabIndex = 2;
            this._labelCamera.Text = "Camera:";
            //
            // _btnSelectCamera
            //
            this._btnSelectCamera.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._btnSelectCamera.Location = new System.Drawing.Point(110, 75);
            this._btnSelectCamera.Name = "_btnSelectCamera";
            this._btnSelectCamera.Size = new System.Drawing.Size(400, 23);
            this._btnSelectCamera.TabIndex = 1;
            this._btnSelectCamera.Text = "Select camera";
            this._btnSelectCamera.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this._btnSelectCamera.UseVisualStyleBackColor = true;
            this._btnSelectCamera.Click += new System.EventHandler(this.BtnSelectCamera_Click);
            //
            // _labelRtmpUrl
            //
            this._labelRtmpUrl.AutoSize = true;
            this._labelRtmpUrl.Location = new System.Drawing.Point(14, 112);
            this._labelRtmpUrl.Name = "_labelRtmpUrl";
            this._labelRtmpUrl.Size = new System.Drawing.Size(66, 13);
            this._labelRtmpUrl.TabIndex = 3;
            this._labelRtmpUrl.Text = "RTMP URL:";
            //
            // _txtRtmpUrl
            //
            this._txtRtmpUrl.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._txtRtmpUrl.Location = new System.Drawing.Point(110, 109);
            this._txtRtmpUrl.Name = "_txtRtmpUrl";
            this._txtRtmpUrl.Size = new System.Drawing.Size(400, 20);
            this._txtRtmpUrl.TabIndex = 2;
            this._txtRtmpUrl.TextChanged += new System.EventHandler(this.OnUserChange);
            //
            // _lblRtmpExamples
            //
            this._lblRtmpExamples.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._lblRtmpExamples.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblRtmpExamples.Location = new System.Drawing.Point(110, 132);
            this._lblRtmpExamples.Name = "_lblRtmpExamples";
            this._lblRtmpExamples.Size = new System.Drawing.Size(400, 30);
            this._lblRtmpExamples.TabIndex = 4;
            this._lblRtmpExamples.Text = "YouTube: rtmp://a.rtmp.youtube.com/live2/STREAM-KEY\rTwitch: rtmps://live.twitch.t" +
    "v/app/STREAM-KEY";
            //
            // _chkEnabled
            //
            this._chkEnabled.AutoSize = true;
            this._chkEnabled.Checked = true;
            this._chkEnabled.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkEnabled.Location = new System.Drawing.Point(110, 170);
            this._chkEnabled.Name = "_chkEnabled";
            this._chkEnabled.Size = new System.Drawing.Size(65, 17);
            this._chkEnabled.TabIndex = 3;
            this._chkEnabled.Text = "Enabled";
            this._chkEnabled.UseVisualStyleBackColor = true;
            this._chkEnabled.CheckedChanged += new System.EventHandler(this.OnUserChange);
            //
            // _chkAllowUntrustedCerts
            //
            this._chkAllowUntrustedCerts.AutoSize = true;
            this._chkAllowUntrustedCerts.Location = new System.Drawing.Point(110, 193);
            this._chkAllowUntrustedCerts.Name = "_chkAllowUntrustedCerts";
            this._chkAllowUntrustedCerts.Size = new System.Drawing.Size(304, 17);
            this._chkAllowUntrustedCerts.TabIndex = 5;
            this._chkAllowUntrustedCerts.Text = "Allow untrusted certificates (for self-signed RTMPS servers)";
            this._chkAllowUntrustedCerts.UseVisualStyleBackColor = true;
            this._chkAllowUntrustedCerts.CheckedChanged += new System.EventHandler(this.OnUserChange);
            //
            // _grpLiveStatus
            //
            this._grpLiveStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._grpLiveStatus.Controls.Add(this._lblStatusLabel);
            this._grpLiveStatus.Controls.Add(this._lblStatusValue);
            this._grpLiveStatus.Controls.Add(this._lblStatsValue);
            this._grpLiveStatus.Controls.Add(this._dgvLog);
            this._grpLiveStatus.Location = new System.Drawing.Point(14, 225);
            this._grpLiveStatus.Name = "_grpLiveStatus";
            this._grpLiveStatus.Size = new System.Drawing.Size(500, 220);
            this._grpLiveStatus.TabIndex = 10;
            this._grpLiveStatus.TabStop = false;
            this._grpLiveStatus.Text = "Live Status";
            //
            // _lblStatusLabel
            //
            this._lblStatusLabel.AutoSize = true;
            this._lblStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this._lblStatusLabel.Location = new System.Drawing.Point(8, 20);
            this._lblStatusLabel.Name = "_lblStatusLabel";
            this._lblStatusLabel.Size = new System.Drawing.Size(43, 13);
            this._lblStatusLabel.TabIndex = 0;
            this._lblStatusLabel.Text = "Status:";
            //
            // _lblStatusValue
            //
            this._lblStatusValue.AutoSize = true;
            this._lblStatusValue.Location = new System.Drawing.Point(60, 20);
            this._lblStatusValue.Name = "_lblStatusValue";
            this._lblStatusValue.Size = new System.Drawing.Size(0, 13);
            this._lblStatusValue.TabIndex = 1;
            //
            // _lblStatsValue
            //
            this._lblStatsValue.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._lblStatsValue.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblStatsValue.Location = new System.Drawing.Point(8, 40);
            this._lblStatsValue.Name = "_lblStatsValue";
            this._lblStatsValue.Size = new System.Drawing.Size(480, 15);
            this._lblStatsValue.TabIndex = 2;
            //
            // _colTime
            //
            this._colTime.HeaderText = "Time";
            this._colTime.Name = "_colTime";
            this._colTime.ReadOnly = true;
            this._colTime.Width = 90;
            //
            // _colLevel
            //
            this._colLevel.HeaderText = "Level";
            this._colLevel.Name = "_colLevel";
            this._colLevel.ReadOnly = true;
            this._colLevel.Width = 55;
            //
            // _colMessage
            //
            this._colMessage.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this._colMessage.HeaderText = "Message";
            this._colMessage.Name = "_colMessage";
            this._colMessage.ReadOnly = true;
            //
            // _dgvLog
            //
            this._dgvLog.AllowUserToAddRows = false;
            this._dgvLog.AllowUserToDeleteRows = false;
            this._dgvLog.AllowUserToResizeRows = false;
            this._dgvLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._dgvLog.BackgroundColor = System.Drawing.SystemColors.Window;
            this._dgvLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._dgvLog.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            this._dgvLog.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this._dgvLog.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this._colTime,
            this._colLevel,
            this._colMessage});
            this._dgvLog.DefaultCellStyle.Font = new System.Drawing.Font("Consolas", 8.25F);
            this._dgvLog.DefaultCellStyle.SelectionBackColor = System.Drawing.SystemColors.Window;
            this._dgvLog.DefaultCellStyle.SelectionForeColor = System.Drawing.SystemColors.ControlText;
            this._dgvLog.EnableHeadersVisualStyles = true;
            this._dgvLog.GridColor = System.Drawing.SystemColors.ControlLight;
            this._dgvLog.Location = new System.Drawing.Point(8, 60);
            this._dgvLog.MultiSelect = false;
            this._dgvLog.Name = "_dgvLog";
            this._dgvLog.ReadOnly = true;
            this._dgvLog.RowHeadersVisible = false;
            this._dgvLog.RowTemplate.Height = 20;
            this._dgvLog.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this._dgvLog.Size = new System.Drawing.Size(480, 150);
            this._dgvLog.TabIndex = 3;
            //
            // StreamConfigUserControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._labelTitle);
            this.Controls.Add(this._labelName);
            this.Controls.Add(this._txtName);
            this.Controls.Add(this._labelCamera);
            this.Controls.Add(this._btnSelectCamera);
            this.Controls.Add(this._labelRtmpUrl);
            this.Controls.Add(this._txtRtmpUrl);
            this.Controls.Add(this._lblRtmpExamples);
            this.Controls.Add(this._chkEnabled);
            this.Controls.Add(this._chkAllowUntrustedCerts);
            this.Controls.Add(this._grpLiveStatus);
            this.Name = "StreamConfigUserControl";
            this.Size = new System.Drawing.Size(530, 460);
            this._grpLiveStatus.ResumeLayout(false);
            this._grpLiveStatus.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._dgvLog)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Label _labelTitle;
        private System.Windows.Forms.Label _labelName;
        private System.Windows.Forms.TextBox _txtName;
        private System.Windows.Forms.Label _labelCamera;
        private System.Windows.Forms.Button _btnSelectCamera;
        private System.Windows.Forms.Label _labelRtmpUrl;
        private System.Windows.Forms.TextBox _txtRtmpUrl;
        private System.Windows.Forms.Label _lblRtmpExamples;
        private System.Windows.Forms.CheckBox _chkEnabled;
        private System.Windows.Forms.CheckBox _chkAllowUntrustedCerts;
        private System.Windows.Forms.GroupBox _grpLiveStatus;
        private System.Windows.Forms.Label _lblStatusLabel;
        private System.Windows.Forms.Label _lblStatusValue;
        private System.Windows.Forms.Label _lblStatsValue;
        private System.Windows.Forms.DataGridView _dgvLog;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colTime;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colLevel;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colMessage;
    }
}
