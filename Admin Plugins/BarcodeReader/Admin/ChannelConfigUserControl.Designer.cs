namespace BarcodeReader.Admin
{
    partial class ChannelConfigUserControl
    {
        private System.ComponentModel.IContainer components = null;

        private void InitializeComponent()
        {
            this._labelTitle = new System.Windows.Forms.Label();

            this._labelName = new System.Windows.Forms.Label();
            this._txtName = new System.Windows.Forms.TextBox();
            this._labelCamera = new System.Windows.Forms.Label();
            this._btnSelectCamera = new System.Windows.Forms.Button();
            this._chkEnabled = new System.Windows.Forms.CheckBox();

            this._twoCol = new System.Windows.Forms.TableLayoutPanel();

            this._grpPreview = new System.Windows.Forms.GroupBox();
            this._pbPreview = new System.Windows.Forms.PictureBox();

            this._grpDecoder = new System.Windows.Forms.GroupBox();
            this._labelFormats = new System.Windows.Forms.Label();
            this._clbFormats = new System.Windows.Forms.CheckedListBox();

            this._labelOptions = new System.Windows.Forms.Label();
            this._chkTryHarder = new System.Windows.Forms.CheckBox();
            this._lblTryHarderDesc = new System.Windows.Forms.Label();
            this._chkAutoRotate = new System.Windows.Forms.CheckBox();
            this._lblAutoRotateDesc = new System.Windows.Forms.Label();
            this._chkTryInverted = new System.Windows.Forms.CheckBox();
            this._lblTryInvertedDesc = new System.Windows.Forms.Label();
            this._chkCreateBookmarks = new System.Windows.Forms.CheckBox();
            this._lblCreateBookmarksDesc = new System.Windows.Forms.Label();

            this._labelThroughput = new System.Windows.Forms.Label();
            this._labelTargetFps = new System.Windows.Forms.Label();
            this._numTargetFps = new System.Windows.Forms.NumericUpDown();
            this._lblTargetFpsDesc = new System.Windows.Forms.Label();
            this._labelDownscale = new System.Windows.Forms.Label();
            this._cboDownscale = new System.Windows.Forms.ComboBox();
            this._lblDownscaleDesc = new System.Windows.Forms.Label();
            this._labelDebounce = new System.Windows.Forms.Label();
            this._numDebounceMs = new System.Windows.Forms.NumericUpDown();
            this._lblDebounceDesc = new System.Windows.Forms.Label();

            this._grpLiveStatus = new System.Windows.Forms.GroupBox();
            this._lblStatusLabel = new System.Windows.Forms.Label();
            this._lblStatusValue = new System.Windows.Forms.Label();
            this._lblStatsValue = new System.Windows.Forms.Label();
            this._lblHint = new System.Windows.Forms.Label();
            this._dgvLog = new System.Windows.Forms.DataGridView();
            this._colMessage = new System.Windows.Forms.DataGridViewTextBoxColumn();

            this._twoCol.SuspendLayout();
            this._grpPreview.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._pbPreview)).BeginInit();
            this._grpDecoder.SuspendLayout();
            this._grpLiveStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._numTargetFps)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this._numDebounceMs)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this._dgvLog)).BeginInit();
            this.SuspendLayout();

            var AnchorTopLeftRight = (System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right);
            var AnchorAll = (System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom
                 | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right);

            //
            // Title + top rows (Name / Camera / Enabled)
            //
            this._labelTitle.AutoSize = true;
            this._labelTitle.Font = new System.Drawing.Font("Segoe UI", 13F, System.Drawing.FontStyle.Bold);
            this._labelTitle.Location = new System.Drawing.Point(16, 14);
            this._labelTitle.Text = "Barcode Channel Configuration";

            this._labelName.AutoSize = true;
            this._labelName.Location = new System.Drawing.Point(18, 54);
            this._labelName.Text = "Name:";

            this._txtName.Anchor = AnchorTopLeftRight;
            this._txtName.Location = new System.Drawing.Point(130, 51);
            this._txtName.Size = new System.Drawing.Size(1100, 23);
            this._txtName.TabIndex = 0;
            this._txtName.TextChanged += new System.EventHandler(this.OnUserChange);

            this._labelCamera.AutoSize = true;
            this._labelCamera.Location = new System.Drawing.Point(18, 84);
            this._labelCamera.Text = "Camera:";

            this._btnSelectCamera.Anchor = AnchorTopLeftRight;
            this._btnSelectCamera.Location = new System.Drawing.Point(130, 80);
            this._btnSelectCamera.Size = new System.Drawing.Size(1100, 27);
            this._btnSelectCamera.Text = "Select camera";
            this._btnSelectCamera.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this._btnSelectCamera.TabIndex = 1;
            this._btnSelectCamera.UseVisualStyleBackColor = true;
            this._btnSelectCamera.Click += new System.EventHandler(this.BtnSelectCamera_Click);

            this._chkEnabled.AutoSize = true;
            this._chkEnabled.Checked = true;
            this._chkEnabled.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkEnabled.Location = new System.Drawing.Point(130, 118);
            this._chkEnabled.Text = "Enabled";
            this._chkEnabled.UseVisualStyleBackColor = true;
            this._chkEnabled.TabIndex = 2;
            this._chkEnabled.CheckedChanged += new System.EventHandler(this.OnUserChange);

            //
            // Two-column area: Camera Preview on the left, a single combined
            // "Decoder" group on the right (formats in its own inner left column,
            // decoder options + throughput fields stacked in its inner right column).
            //
            this._twoCol.Anchor = AnchorTopLeftRight;
            this._twoCol.Location = new System.Drawing.Point(16, 145);
            this._twoCol.Size = new System.Drawing.Size(1216, 540);
            this._twoCol.ColumnCount = 2;
            this._twoCol.RowCount = 1;
            // 40/60 split so the preview doesn't eat the whole right side on ultra-wide
            // detail panes. PictureBox.Zoom letterboxes inside the 40% cell to keep aspect.
            this._twoCol.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 40F));
            this._twoCol.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 60F));
            this._twoCol.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this._twoCol.Padding = new System.Windows.Forms.Padding(0);
            this._twoCol.Controls.Add(this._grpPreview, 0, 0);
            this._twoCol.Controls.Add(this._grpDecoder, 1, 0);

            //
            // Camera Preview (left column)
            //
            this._grpPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this._grpPreview.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this._grpPreview.Text = "Camera Preview";
            this._grpPreview.Controls.Add(this._pbPreview);

            this._pbPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this._pbPreview.BackColor = System.Drawing.Color.Black;
            this._pbPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._pbPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this._pbPreview.TabStop = false;
            this._pbPreview.Padding = new System.Windows.Forms.Padding(0);

            //
            // Decoder group (right column): two inner columns. Left = barcode-format
            // checklist. Right = decoder options stacked over throughput controls.
            // Every control anchors to the group so the right inner column grows with
            // the detail pane width and descriptions don't get clipped.
            //
            this._grpDecoder.Dock = System.Windows.Forms.DockStyle.Fill;
            this._grpDecoder.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this._grpDecoder.Text = "Decoder";
            this._grpDecoder.Controls.Add(this._labelFormats);
            this._grpDecoder.Controls.Add(this._clbFormats);
            this._grpDecoder.Controls.Add(this._labelOptions);
            this._grpDecoder.Controls.Add(this._chkTryHarder);
            this._grpDecoder.Controls.Add(this._lblTryHarderDesc);
            this._grpDecoder.Controls.Add(this._chkAutoRotate);
            this._grpDecoder.Controls.Add(this._lblAutoRotateDesc);
            this._grpDecoder.Controls.Add(this._chkTryInverted);
            this._grpDecoder.Controls.Add(this._lblTryInvertedDesc);
            this._grpDecoder.Controls.Add(this._chkCreateBookmarks);
            this._grpDecoder.Controls.Add(this._lblCreateBookmarksDesc);
            this._grpDecoder.Controls.Add(this._labelThroughput);
            this._grpDecoder.Controls.Add(this._labelTargetFps);
            this._grpDecoder.Controls.Add(this._numTargetFps);
            this._grpDecoder.Controls.Add(this._lblTargetFpsDesc);
            this._grpDecoder.Controls.Add(this._labelDownscale);
            this._grpDecoder.Controls.Add(this._cboDownscale);
            this._grpDecoder.Controls.Add(this._lblDownscaleDesc);
            this._grpDecoder.Controls.Add(this._labelDebounce);
            this._grpDecoder.Controls.Add(this._numDebounceMs);
            this._grpDecoder.Controls.Add(this._lblDebounceDesc);

            // Left inner column: formats checklist, anchored full-height
            this._labelFormats.AutoSize = true;
            this._labelFormats.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this._labelFormats.Location = new System.Drawing.Point(12, 24);
            this._labelFormats.Text = "Barcode formats to recognize";

            // Fixed-size list sized to fit all 13 formats exactly. Anchor to Top+Left only
            // so the box doesn't stretch vertically and show empty rows under the last item.
            this._clbFormats.Anchor = (System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left);
            this._clbFormats.Location = new System.Drawing.Point(12, 46);
            this._clbFormats.Size = new System.Drawing.Size(240, 226);
            this._clbFormats.CheckOnClick = true;
            this._clbFormats.IntegralHeight = true;
            this._clbFormats.MultiColumn = false;
            this._clbFormats.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(this._clbFormats_ItemCheck);

            // Right inner column positions.
            // OptDescX sits 18 px past RightX so descriptions line up with the checkbox TEXT
            // (the checkbox square occupies ~16 px before the label starts). Throughput rows
            // below use RightX directly because their leading control is a Label, not a
            // checkbox  keeps the vertical alignment consistent per-section.
            const int RightX = 272;
            const int OptDescX = RightX + 18;
            const int OptDescOffsetY = 22;
            const int OptStep = 52;

            // --- Decoder options section ---
            this._labelOptions.AutoSize = true;
            this._labelOptions.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this._labelOptions.Location = new System.Drawing.Point(RightX, 24);
            this._labelOptions.Text = "Decoder options";

            int y = 48;

            this._chkTryHarder.AutoSize = true;
            this._chkTryHarder.Location = new System.Drawing.Point(RightX, y);
            this._chkTryHarder.Text = "Try Harder";
            this._chkTryHarder.UseVisualStyleBackColor = true;
            this._chkTryHarder.CheckedChanged += new System.EventHandler(this.OnUserChange);

            this._lblTryHarderDesc.Anchor = AnchorTopLeftRight;
            this._lblTryHarderDesc.Location = new System.Drawing.Point(OptDescX, y + OptDescOffsetY);
            this._lblTryHarderDesc.Size = new System.Drawing.Size(320, 20);
            this._lblTryHarderDesc.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblTryHarderDesc.Text = "Spend more CPU per frame to catch low-contrast, partially blocked, or rotated codes.";
            y += OptStep;

            this._chkAutoRotate.AutoSize = true;
            this._chkAutoRotate.Location = new System.Drawing.Point(RightX, y);
            this._chkAutoRotate.Text = "Auto Rotate";
            this._chkAutoRotate.UseVisualStyleBackColor = true;
            this._chkAutoRotate.CheckedChanged += new System.EventHandler(this.OnUserChange);

            this._lblAutoRotateDesc.Anchor = AnchorTopLeftRight;
            this._lblAutoRotateDesc.Location = new System.Drawing.Point(OptDescX, y + OptDescOffsetY);
            this._lblAutoRotateDesc.Size = new System.Drawing.Size(320, 20);
            this._lblAutoRotateDesc.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblAutoRotateDesc.Text = "Retry each frame rotated 90/180/270 deg. Useful for cameras mounted sideways.";
            y += OptStep;

            this._chkTryInverted.AutoSize = true;
            this._chkTryInverted.Location = new System.Drawing.Point(RightX, y);
            this._chkTryInverted.Text = "Try Inverted";
            this._chkTryInverted.UseVisualStyleBackColor = true;
            this._chkTryInverted.CheckedChanged += new System.EventHandler(this.OnUserChange);

            this._lblTryInvertedDesc.Anchor = AnchorTopLeftRight;
            this._lblTryInvertedDesc.Location = new System.Drawing.Point(OptDescX, y + OptDescOffsetY);
            this._lblTryInvertedDesc.Size = new System.Drawing.Size(320, 20);
            this._lblTryInvertedDesc.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblTryInvertedDesc.Text = "Retry with colors inverted so white-on-black codes decode correctly.";
            y += OptStep;

            this._chkCreateBookmarks.AutoSize = true;
            this._chkCreateBookmarks.Checked = true;
            this._chkCreateBookmarks.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkCreateBookmarks.Location = new System.Drawing.Point(RightX, y);
            this._chkCreateBookmarks.Text = "Create bookmarks for detections";
            this._chkCreateBookmarks.UseVisualStyleBackColor = true;
            this._chkCreateBookmarks.CheckedChanged += new System.EventHandler(this.OnUserChange);

            this._lblCreateBookmarksDesc.Anchor = AnchorTopLeftRight;
            this._lblCreateBookmarksDesc.Location = new System.Drawing.Point(OptDescX, y + OptDescOffsetY);
            this._lblCreateBookmarksDesc.Size = new System.Drawing.Size(560, 20);
            this._lblCreateBookmarksDesc.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblCreateBookmarksDesc.Text = "Add a searchable bookmark on the camera timeline (2 s pre/post) for each decoded barcode.";
            y += OptStep + 12;

            // --- Throughput section (continuing in the right inner column) ---
            const int ThrLabelX = RightX;
            const int ThrControlX = RightX + 160;
            const int ThrRowHeight = 48;

            this._labelThroughput.AutoSize = true;
            this._labelThroughput.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this._labelThroughput.Location = new System.Drawing.Point(RightX, y);
            this._labelThroughput.Text = "Throughput";
            y += 24;

            this._labelTargetFps.AutoSize = true;
            this._labelTargetFps.Location = new System.Drawing.Point(ThrLabelX, y + 3);
            this._labelTargetFps.Text = "Target frame rate:";

            this._numTargetFps.Location = new System.Drawing.Point(ThrControlX, y);
            this._numTargetFps.Size = new System.Drawing.Size(80, 23);
            this._numTargetFps.Minimum = 1;
            this._numTargetFps.Maximum = 60;
            this._numTargetFps.Value = 1;
            this._numTargetFps.ValueChanged += new System.EventHandler(this.OnUserChange);

            this._lblTargetFpsDesc.Anchor = AnchorTopLeftRight;
            this._lblTargetFpsDesc.Location = new System.Drawing.Point(RightX, y + 26);
            this._lblTargetFpsDesc.Size = new System.Drawing.Size(560, 18);
            this._lblTargetFpsDesc.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblTargetFpsDesc.Text = "Max decodes per second (also capped by camera's live FPS and decode time).";
            y += ThrRowHeight;

            this._labelDownscale.AutoSize = true;
            this._labelDownscale.Location = new System.Drawing.Point(ThrLabelX, y + 3);
            this._labelDownscale.Text = "Downscale to width:";

            this._cboDownscale.Location = new System.Drawing.Point(ThrControlX, y);
            this._cboDownscale.Size = new System.Drawing.Size(180, 23);
            this._cboDownscale.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._cboDownscale.SelectedIndexChanged += new System.EventHandler(this.OnUserChange);

            this._lblDownscaleDesc.Anchor = AnchorTopLeftRight;
            this._lblDownscaleDesc.Location = new System.Drawing.Point(RightX, y + 26);
            this._lblDownscaleDesc.Size = new System.Drawing.Size(560, 18);
            this._lblDownscaleDesc.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblDownscaleDesc.Text = "Resize each frame before decoding. Big speed-up for 4K/8K cameras.";
            y += ThrRowHeight;

            this._labelDebounce.AutoSize = true;
            this._labelDebounce.Location = new System.Drawing.Point(ThrLabelX, y + 3);
            this._labelDebounce.Text = "Duplicate debounce:";

            this._numDebounceMs.Location = new System.Drawing.Point(ThrControlX, y);
            this._numDebounceMs.Size = new System.Drawing.Size(80, 23);
            this._numDebounceMs.Minimum = 0;
            this._numDebounceMs.Maximum = 60000;
            this._numDebounceMs.Increment = 100;
            this._numDebounceMs.Value = 2000;
            this._numDebounceMs.ValueChanged += new System.EventHandler(this.OnUserChange);

            this._lblDebounceDesc.Anchor = AnchorTopLeftRight;
            this._lblDebounceDesc.Location = new System.Drawing.Point(RightX, y + 26);
            this._lblDebounceDesc.Size = new System.Drawing.Size(560, 18);
            this._lblDebounceDesc.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblDebounceDesc.Text = "ms. Suppresses repeated detections of the same text. 0 logs every frame.";

            //
            // Live Status (full width, fills remaining space at bottom)
            //
            this._grpLiveStatus.Anchor = AnchorAll;
            this._grpLiveStatus.Location = new System.Drawing.Point(16, 695);
            this._grpLiveStatus.Size = new System.Drawing.Size(1216, 260);
            this._grpLiveStatus.Text = "Live Status";
            this._grpLiveStatus.Controls.Add(this._lblStatusLabel);
            this._grpLiveStatus.Controls.Add(this._lblStatusValue);
            this._grpLiveStatus.Controls.Add(this._lblStatsValue);
            this._grpLiveStatus.Controls.Add(this._lblHint);
            this._grpLiveStatus.Controls.Add(this._dgvLog);

            this._lblStatusLabel.AutoSize = true;
            this._lblStatusLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this._lblStatusLabel.Location = new System.Drawing.Point(12, 26);
            this._lblStatusLabel.Text = "Status:";

            this._lblStatusValue.AutoSize = true;
            this._lblStatusValue.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._lblStatusValue.Location = new System.Drawing.Point(70, 26);

            this._lblStatsValue.Anchor = AnchorTopLeftRight;
            this._lblStatsValue.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblStatsValue.Location = new System.Drawing.Point(12, 50);
            this._lblStatsValue.Size = new System.Drawing.Size(1196, 18);

            this._lblHint.Anchor = AnchorTopLeftRight;
            this._lblHint.Location = new System.Drawing.Point(12, 72);
            this._lblHint.Size = new System.Drawing.Size(1196, 18);

            this._colMessage.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this._colMessage.HeaderText = "Helper log (last 40 lines incl. detections)";
            this._colMessage.Name = "_colMessage";
            this._colMessage.ReadOnly = true;

            this._dgvLog.AllowUserToAddRows = false;
            this._dgvLog.AllowUserToDeleteRows = false;
            this._dgvLog.AllowUserToResizeRows = false;
            this._dgvLog.Anchor = AnchorAll;
            this._dgvLog.BackgroundColor = System.Drawing.SystemColors.Window;
            this._dgvLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._dgvLog.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            this._dgvLog.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this._dgvLog.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] { this._colMessage });
            this._dgvLog.DefaultCellStyle.Font = new System.Drawing.Font("Consolas", 9F);
            this._dgvLog.DefaultCellStyle.SelectionBackColor = System.Drawing.SystemColors.Window;
            this._dgvLog.DefaultCellStyle.SelectionForeColor = System.Drawing.SystemColors.ControlText;
            this._dgvLog.EnableHeadersVisualStyles = true;
            this._dgvLog.GridColor = System.Drawing.SystemColors.ControlLight;
            this._dgvLog.Location = new System.Drawing.Point(12, 98);
            this._dgvLog.MultiSelect = false;
            this._dgvLog.ReadOnly = true;
            this._dgvLog.RowHeadersVisible = false;
            this._dgvLog.RowTemplate.Height = 20;
            this._dgvLog.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this._dgvLog.Size = new System.Drawing.Size(1196, 150);

            //
            // ChannelConfigUserControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.Controls.Add(this._labelTitle);
            this.Controls.Add(this._labelName);
            this.Controls.Add(this._txtName);
            this.Controls.Add(this._labelCamera);
            this.Controls.Add(this._btnSelectCamera);
            this.Controls.Add(this._chkEnabled);
            this.Controls.Add(this._twoCol);
            this.Controls.Add(this._grpLiveStatus);
            this.Name = "ChannelConfigUserControl";
            this.Size = new System.Drawing.Size(1248, 970);

            this._twoCol.ResumeLayout(false);
            this._grpPreview.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this._pbPreview)).EndInit();
            this._grpDecoder.ResumeLayout(false);
            this._grpDecoder.PerformLayout();
            this._grpLiveStatus.ResumeLayout(false);
            this._grpLiveStatus.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._numTargetFps)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this._numDebounceMs)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this._dgvLog)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // CheckedListBox fires ItemCheck BEFORE the state updates AND during the constructor
        // when we populate the list. Only forward real user edits - if the handle doesn't
        // exist yet we're still initialising; if we can invoke, defer so the check state has
        // actually flipped by the time OnUserChange reads it.
        private void _clbFormats_ItemCheck(object sender, System.Windows.Forms.ItemCheckEventArgs e)
        {
            if (!this.IsHandleCreated) return;
            this.BeginInvoke(new System.Action(() => this.OnUserChange(sender, System.EventArgs.Empty)));
        }

        private System.Windows.Forms.Label _labelTitle;
        private System.Windows.Forms.Label _labelName;
        private System.Windows.Forms.TextBox _txtName;
        private System.Windows.Forms.Label _labelCamera;
        private System.Windows.Forms.Button _btnSelectCamera;
        private System.Windows.Forms.CheckBox _chkEnabled;

        private System.Windows.Forms.TableLayoutPanel _twoCol;

        private System.Windows.Forms.GroupBox _grpPreview;
        private System.Windows.Forms.PictureBox _pbPreview;

        private System.Windows.Forms.GroupBox _grpDecoder;
        private System.Windows.Forms.Label _labelFormats;
        private System.Windows.Forms.CheckedListBox _clbFormats;
        private System.Windows.Forms.Label _labelOptions;
        private System.Windows.Forms.CheckBox _chkTryHarder;
        private System.Windows.Forms.Label _lblTryHarderDesc;
        private System.Windows.Forms.CheckBox _chkAutoRotate;
        private System.Windows.Forms.Label _lblAutoRotateDesc;
        private System.Windows.Forms.CheckBox _chkTryInverted;
        private System.Windows.Forms.Label _lblTryInvertedDesc;
        private System.Windows.Forms.CheckBox _chkCreateBookmarks;
        private System.Windows.Forms.Label _lblCreateBookmarksDesc;

        private System.Windows.Forms.Label _labelThroughput;
        private System.Windows.Forms.Label _labelTargetFps;
        private System.Windows.Forms.NumericUpDown _numTargetFps;
        private System.Windows.Forms.Label _lblTargetFpsDesc;
        private System.Windows.Forms.Label _labelDownscale;
        private System.Windows.Forms.ComboBox _cboDownscale;
        private System.Windows.Forms.Label _lblDownscaleDesc;
        private System.Windows.Forms.Label _labelDebounce;
        private System.Windows.Forms.NumericUpDown _numDebounceMs;
        private System.Windows.Forms.Label _lblDebounceDesc;

        private System.Windows.Forms.GroupBox _grpLiveStatus;
        private System.Windows.Forms.Label _lblStatusLabel;
        private System.Windows.Forms.Label _lblStatusValue;
        private System.Windows.Forms.Label _lblStatsValue;
        private System.Windows.Forms.Label _lblHint;
        private System.Windows.Forms.DataGridView _dgvLog;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colMessage;
    }
}
