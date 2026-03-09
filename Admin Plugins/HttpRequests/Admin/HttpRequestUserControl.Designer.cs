namespace HttpRequests.Admin
{
    partial class HttpRequestUserControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this._labelTitle = new System.Windows.Forms.Label();
            this._btnDuplicate = new System.Windows.Forms.Button();
            this._grpRequest = new System.Windows.Forms.GroupBox();
            this._labelName = new System.Windows.Forms.Label();
            this._txtName = new System.Windows.Forms.TextBox();
            this._labelMethod = new System.Windows.Forms.Label();
            this._cboMethod = new System.Windows.Forms.ComboBox();
            this._labelUrl = new System.Windows.Forms.Label();
            this._txtUrl = new System.Windows.Forms.TextBox();
            this._chkEnabled = new System.Windows.Forms.CheckBox();
            this._grpBody = new System.Windows.Forms.GroupBox();
            this._labelPayloadType = new System.Windows.Forms.Label();
            this._cboPayloadType = new System.Windows.Forms.ComboBox();
            this._txtPayload = new FastColoredTextBoxNS.FastColoredTextBox();
            this._chkIncludeEventData = new System.Windows.Forms.CheckBox();
            this._tabControl = new System.Windows.Forms.TabControl();
            this._tabUrlParams = new System.Windows.Forms.TabPage();
            this._dgvQueryParams = new System.Windows.Forms.DataGridView();
            this._colQpKey = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this._colQpValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this._btnRemoveQueryParam = new System.Windows.Forms.Button();
            this._tabHeaders = new System.Windows.Forms.TabPage();
            this._dgvHeaders = new System.Windows.Forms.DataGridView();
            this._colHdrKey = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this._colHdrValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this._btnRemoveHeader = new System.Windows.Forms.Button();
            this._tabAuth = new System.Windows.Forms.TabPage();
            this._labelAuthType = new System.Windows.Forms.Label();
            this._cboAuthType = new System.Windows.Forms.ComboBox();
            this._lblAuthUsername = new System.Windows.Forms.Label();
            this._txtAuthUsername = new System.Windows.Forms.TextBox();
            this._lblAuthPassword = new System.Windows.Forms.Label();
            this._txtAuthPassword = new System.Windows.Forms.TextBox();
            this._lblAuthToken = new System.Windows.Forms.Label();
            this._txtAuthToken = new System.Windows.Forms.TextBox();
            this._tabOptions = new System.Windows.Forms.TabPage();
            this._labelTimeout = new System.Windows.Forms.Label();
            this._txtTimeout = new System.Windows.Forms.TextBox();
            this._lblTimeoutUnit = new System.Windows.Forms.Label();
            this._chkSkipCertValidation = new System.Windows.Forms.CheckBox();
            this._btnTest = new System.Windows.Forms.Button();
            this._grpTestResult = new System.Windows.Forms.GroupBox();
            this._lblTestStatus = new System.Windows.Forms.Label();
            this._txtTestResponse = new System.Windows.Forms.TextBox();
            this._grpRequest.SuspendLayout();
            this._grpBody.SuspendLayout();
            this._tabControl.SuspendLayout();
            this._tabUrlParams.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._dgvQueryParams)).BeginInit();
            this._tabHeaders.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._dgvHeaders)).BeginInit();
            this._tabAuth.SuspendLayout();
            this._tabOptions.SuspendLayout();
            this._grpTestResult.SuspendLayout();
            this.SuspendLayout();
            //
            // _labelTitle
            //
            this._labelTitle.AutoSize = true;
            this._labelTitle.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this._labelTitle.Location = new System.Drawing.Point(12, 10);
            this._labelTitle.Name = "_labelTitle";
            this._labelTitle.TabIndex = 0;
            this._labelTitle.Text = "HTTP Request Configuration";
            //
            // _btnDuplicate
            //
            this._btnDuplicate.Location = new System.Drawing.Point(410, 10);
            this._btnDuplicate.Name = "_btnDuplicate";
            this._btnDuplicate.Size = new System.Drawing.Size(90, 24);
            this._btnDuplicate.TabIndex = 99;
            this._btnDuplicate.Text = "Duplicate";
            this._btnDuplicate.UseVisualStyleBackColor = true;
            this._btnDuplicate.Click += new System.EventHandler(this.OnDuplicateClick);
            // ════════════════════════════════════════════════
            // LEFT COLUMN - Config (x=12, width=490)
            // ════════════════════════════════════════════════
            //
            // _grpRequest
            //
            this._grpRequest.Controls.Add(this._labelName);
            this._grpRequest.Controls.Add(this._txtName);
            this._grpRequest.Controls.Add(this._labelMethod);
            this._grpRequest.Controls.Add(this._cboMethod);
            this._grpRequest.Controls.Add(this._labelUrl);
            this._grpRequest.Controls.Add(this._txtUrl);
            this._grpRequest.Controls.Add(this._chkEnabled);
            this._grpRequest.Location = new System.Drawing.Point(12, 38);
            this._grpRequest.Name = "_grpRequest";
            this._grpRequest.Size = new System.Drawing.Size(490, 120);
            this._grpRequest.TabIndex = 1;
            this._grpRequest.TabStop = false;
            this._grpRequest.Text = "Request";
            //
            // _labelName
            //
            this._labelName.AutoSize = true;
            this._labelName.Location = new System.Drawing.Point(10, 24);
            this._labelName.Name = "_labelName";
            this._labelName.TabIndex = 0;
            this._labelName.Text = "Name:";
            //
            // _txtName
            //
            this._txtName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this._txtName.Location = new System.Drawing.Point(60, 21);
            this._txtName.Name = "_txtName";
            this._txtName.Size = new System.Drawing.Size(420, 20);
            this._txtName.TabIndex = 1;
            this._txtName.TextChanged += new System.EventHandler(this.OnUserChange);
            //
            // _labelMethod
            //
            this._labelMethod.AutoSize = true;
            this._labelMethod.Location = new System.Drawing.Point(10, 51);
            this._labelMethod.Name = "_labelMethod";
            this._labelMethod.TabIndex = 2;
            this._labelMethod.Text = "Method:";
            //
            // _cboMethod
            //
            this._cboMethod.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._cboMethod.Items.AddRange(new object[] { "GET", "POST", "PUT", "DELETE", "PATCH" });
            this._cboMethod.Location = new System.Drawing.Point(60, 48);
            this._cboMethod.Name = "_cboMethod";
            this._cboMethod.Size = new System.Drawing.Size(100, 21);
            this._cboMethod.TabIndex = 3;
            this._cboMethod.SelectedIndexChanged += new System.EventHandler(this.OnMethodChanged);
            //
            // _labelUrl
            //
            this._labelUrl.AutoSize = true;
            this._labelUrl.Location = new System.Drawing.Point(10, 77);
            this._labelUrl.Name = "_labelUrl";
            this._labelUrl.TabIndex = 4;
            this._labelUrl.Text = "URL:";
            //
            // _txtUrl
            //
            this._txtUrl.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this._txtUrl.Location = new System.Drawing.Point(60, 74);
            this._txtUrl.Name = "_txtUrl";
            this._txtUrl.Size = new System.Drawing.Size(420, 20);
            this._txtUrl.TabIndex = 5;
            this._txtUrl.TextChanged += new System.EventHandler(this.OnUserChange);
            //
            // _chkEnabled
            //
            this._chkEnabled.AutoSize = true;
            this._chkEnabled.Checked = true;
            this._chkEnabled.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkEnabled.Location = new System.Drawing.Point(10, 100);
            this._chkEnabled.Name = "_chkEnabled";
            this._chkEnabled.Size = new System.Drawing.Size(65, 17);
            this._chkEnabled.TabIndex = 6;
            this._chkEnabled.Text = "Enabled";
            this._chkEnabled.UseVisualStyleBackColor = true;
            this._chkEnabled.CheckedChanged += new System.EventHandler(this.OnUserChange);
            // ────────────────────────────────────────────────
            // Body
            // ────────────────────────────────────────────────
            //
            // _grpBody
            //
            this._lblJsonHint = new System.Windows.Forms.Label();
            this._grpBody.Controls.Add(this._labelPayloadType);
            this._grpBody.Controls.Add(this._cboPayloadType);
            this._grpBody.Controls.Add(this._txtPayload);
            this._grpBody.Controls.Add(this._lblJsonHint);
            this._grpBody.Controls.Add(this._chkIncludeEventData);
            this._grpBody.Location = new System.Drawing.Point(12, 164);
            this._grpBody.Name = "_grpBody";
            this._grpBody.Size = new System.Drawing.Size(490, 168);
            this._grpBody.TabIndex = 2;
            this._grpBody.TabStop = false;
            this._grpBody.Text = "Body";
            //
            // _labelPayloadType
            //
            this._labelPayloadType.AutoSize = true;
            this._labelPayloadType.Location = new System.Drawing.Point(10, 24);
            this._labelPayloadType.Name = "_labelPayloadType";
            this._labelPayloadType.TabIndex = 0;
            this._labelPayloadType.Text = "Type:";
            //
            // _cboPayloadType
            //
            this._cboPayloadType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._cboPayloadType.Items.AddRange(new object[] { "None", "JSON", "Plain Text", "XML", "CSV" });
            this._cboPayloadType.Location = new System.Drawing.Point(50, 21);
            this._cboPayloadType.Name = "_cboPayloadType";
            this._cboPayloadType.Size = new System.Drawing.Size(120, 21);
            this._cboPayloadType.TabIndex = 1;
            this._cboPayloadType.SelectedIndexChanged += new System.EventHandler(this.OnPayloadTypeChanged);
            //
            // _txtPayload
            //
            this._txtPayload.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this._txtPayload.Font = new System.Drawing.Font("Consolas", 9.75F);
            this._txtPayload.Location = new System.Drawing.Point(10, 50);
            this._txtPayload.Name = "_txtPayload";
            this._txtPayload.Size = new System.Drawing.Size(470, 85);
            this._txtPayload.TabIndex = 2;
            this._txtPayload.Language = FastColoredTextBoxNS.Language.Custom;
            this._txtPayload.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._txtPayload.ShowLineNumbers = true;
            this._txtPayload.TextChanged += new System.EventHandler<FastColoredTextBoxNS.TextChangedEventArgs>(this.OnPayloadTextChanged);
            //
            // _lblJsonHint
            //
            this._lblJsonHint.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Right)));
            this._lblJsonHint.Font = new System.Drawing.Font("Segoe UI", 8F);
            this._lblJsonHint.ForeColor = System.Drawing.Color.Red;
            this._lblJsonHint.Location = new System.Drawing.Point(180, 24);
            this._lblJsonHint.Name = "_lblJsonHint";
            this._lblJsonHint.Size = new System.Drawing.Size(300, 15);
            this._lblJsonHint.TabIndex = 4;
            this._lblJsonHint.Text = "";
            this._lblJsonHint.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // _chkIncludeEventData
            //
            this._chkIncludeEventData.AutoSize = true;
            this._chkIncludeEventData.Checked = true;
            this._chkIncludeEventData.CheckState = System.Windows.Forms.CheckState.Checked;
            this._chkIncludeEventData.Location = new System.Drawing.Point(10, 142);
            this._chkIncludeEventData.Name = "_chkIncludeEventData";
            this._chkIncludeEventData.Size = new System.Drawing.Size(295, 17);
            this._chkIncludeEventData.TabIndex = 3;
            this._chkIncludeEventData.Text = "Include Milestone event data in payload (merge)";
            this._chkIncludeEventData.UseVisualStyleBackColor = true;
            this._chkIncludeEventData.CheckedChanged += new System.EventHandler(this.OnUserChange);
            // ────────────────────────────────────────────────
            // TabControl
            // ────────────────────────────────────────────────
            //
            // _tabControl
            //
            this._tabControl.Controls.Add(this._tabUrlParams);
            this._tabControl.Controls.Add(this._tabHeaders);
            this._tabControl.Controls.Add(this._tabAuth);
            this._tabControl.Controls.Add(this._tabOptions);
            this._tabControl.Location = new System.Drawing.Point(12, 338);
            this._tabControl.Name = "_tabControl";
            this._tabControl.SelectedIndex = 0;
            this._tabControl.Size = new System.Drawing.Size(490, 180);
            this._tabControl.TabIndex = 3;
            // ── Tab: URL Parameters ──
            //
            // _tabUrlParams
            //
            this._tabUrlParams.Controls.Add(this._dgvQueryParams);
            this._tabUrlParams.Controls.Add(this._btnRemoveQueryParam);
            this._tabUrlParams.Location = new System.Drawing.Point(4, 22);
            this._tabUrlParams.Name = "_tabUrlParams";
            this._tabUrlParams.Padding = new System.Windows.Forms.Padding(6);
            this._tabUrlParams.Size = new System.Drawing.Size(482, 154);
            this._tabUrlParams.TabIndex = 0;
            this._tabUrlParams.Text = "URL Parameters";
            this._tabUrlParams.UseVisualStyleBackColor = true;
            //
            // _colQpKey
            //
            this._colQpKey.FillWeight = 40F;
            this._colQpKey.HeaderText = "Key";
            this._colQpKey.Name = "QpKey";
            //
            // _colQpValue
            //
            this._colQpValue.FillWeight = 60F;
            this._colQpValue.HeaderText = "Value";
            this._colQpValue.Name = "QpValue";
            //
            // _dgvQueryParams
            //
            this._dgvQueryParams.AllowUserToAddRows = true;
            this._dgvQueryParams.AllowUserToDeleteRows = false;
            this._dgvQueryParams.AllowUserToResizeRows = false;
            this._dgvQueryParams.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this._dgvQueryParams.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this._dgvQueryParams.BackgroundColor = System.Drawing.SystemColors.Window;
            this._dgvQueryParams.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._dgvQueryParams.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this._dgvQueryParams.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this._colQpKey,
            this._colQpValue});
            this._dgvQueryParams.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            this._dgvQueryParams.Location = new System.Drawing.Point(6, 6);
            this._dgvQueryParams.MultiSelect = false;
            this._dgvQueryParams.Name = "_dgvQueryParams";
            this._dgvQueryParams.RowHeadersVisible = false;
            this._dgvQueryParams.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this._dgvQueryParams.Size = new System.Drawing.Size(470, 115);
            this._dgvQueryParams.TabIndex = 0;
            this._dgvQueryParams.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.OnGridCellEndEdit);
            //
            // _btnRemoveQueryParam
            //
            this._btnRemoveQueryParam.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom
            | System.Windows.Forms.AnchorStyles.Left)));
            this._btnRemoveQueryParam.Location = new System.Drawing.Point(6, 125);
            this._btnRemoveQueryParam.Name = "_btnRemoveQueryParam";
            this._btnRemoveQueryParam.Size = new System.Drawing.Size(70, 23);
            this._btnRemoveQueryParam.TabIndex = 1;
            this._btnRemoveQueryParam.Text = "Remove";
            this._btnRemoveQueryParam.UseVisualStyleBackColor = true;
            this._btnRemoveQueryParam.Click += new System.EventHandler(this.OnRemoveQueryParam);
            // ── Tab: Headers ──
            //
            // _tabHeaders
            //
            this._tabHeaders.Controls.Add(this._dgvHeaders);
            this._tabHeaders.Controls.Add(this._btnRemoveHeader);
            this._tabHeaders.Location = new System.Drawing.Point(4, 22);
            this._tabHeaders.Name = "_tabHeaders";
            this._tabHeaders.Padding = new System.Windows.Forms.Padding(6);
            this._tabHeaders.Size = new System.Drawing.Size(482, 154);
            this._tabHeaders.TabIndex = 1;
            this._tabHeaders.Text = "Headers";
            this._tabHeaders.UseVisualStyleBackColor = true;
            //
            // _colHdrKey
            //
            this._colHdrKey.FillWeight = 40F;
            this._colHdrKey.HeaderText = "Key";
            this._colHdrKey.Name = "HdrKey";
            //
            // _colHdrValue
            //
            this._colHdrValue.FillWeight = 60F;
            this._colHdrValue.HeaderText = "Value";
            this._colHdrValue.Name = "HdrValue";
            //
            // _dgvHeaders
            //
            this._dgvHeaders.AllowUserToAddRows = true;
            this._dgvHeaders.AllowUserToDeleteRows = false;
            this._dgvHeaders.AllowUserToResizeRows = false;
            this._dgvHeaders.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this._dgvHeaders.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this._dgvHeaders.BackgroundColor = System.Drawing.SystemColors.Window;
            this._dgvHeaders.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._dgvHeaders.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this._dgvHeaders.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this._colHdrKey,
            this._colHdrValue});
            this._dgvHeaders.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            this._dgvHeaders.Location = new System.Drawing.Point(6, 6);
            this._dgvHeaders.MultiSelect = false;
            this._dgvHeaders.Name = "_dgvHeaders";
            this._dgvHeaders.RowHeadersVisible = false;
            this._dgvHeaders.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this._dgvHeaders.Size = new System.Drawing.Size(470, 115);
            this._dgvHeaders.TabIndex = 0;
            this._dgvHeaders.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.OnGridCellEndEdit);
            //
            // _btnRemoveHeader
            //
            this._btnRemoveHeader.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom
            | System.Windows.Forms.AnchorStyles.Left)));
            this._btnRemoveHeader.Location = new System.Drawing.Point(6, 125);
            this._btnRemoveHeader.Name = "_btnRemoveHeader";
            this._btnRemoveHeader.Size = new System.Drawing.Size(70, 23);
            this._btnRemoveHeader.TabIndex = 1;
            this._btnRemoveHeader.Text = "Remove";
            this._btnRemoveHeader.UseVisualStyleBackColor = true;
            this._btnRemoveHeader.Click += new System.EventHandler(this.OnRemoveHeader);
            // ── Tab: Authorization ──
            //
            // _tabAuth
            //
            this._tabAuth.Controls.Add(this._labelAuthType);
            this._tabAuth.Controls.Add(this._cboAuthType);
            this._tabAuth.Controls.Add(this._lblAuthUsername);
            this._tabAuth.Controls.Add(this._txtAuthUsername);
            this._tabAuth.Controls.Add(this._lblAuthPassword);
            this._tabAuth.Controls.Add(this._txtAuthPassword);
            this._tabAuth.Controls.Add(this._lblAuthToken);
            this._tabAuth.Controls.Add(this._txtAuthToken);
            this._tabAuth.Location = new System.Drawing.Point(4, 22);
            this._tabAuth.Name = "_tabAuth";
            this._tabAuth.Padding = new System.Windows.Forms.Padding(6);
            this._tabAuth.Size = new System.Drawing.Size(482, 154);
            this._tabAuth.TabIndex = 2;
            this._tabAuth.Text = "Authorization";
            this._tabAuth.UseVisualStyleBackColor = true;
            //
            // _labelAuthType
            //
            this._labelAuthType.AutoSize = true;
            this._labelAuthType.Location = new System.Drawing.Point(10, 14);
            this._labelAuthType.Name = "_labelAuthType";
            this._labelAuthType.TabIndex = 0;
            this._labelAuthType.Text = "Type:";
            //
            // _cboAuthType
            //
            this._cboAuthType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._cboAuthType.Items.AddRange(new object[] { "None", "Basic", "Bearer", "Digest" });
            this._cboAuthType.Location = new System.Drawing.Point(80, 11);
            this._cboAuthType.Name = "_cboAuthType";
            this._cboAuthType.Size = new System.Drawing.Size(120, 21);
            this._cboAuthType.TabIndex = 1;
            this._cboAuthType.SelectedIndexChanged += new System.EventHandler(this.OnAuthTypeChanged);
            //
            // _lblAuthUsername
            //
            this._lblAuthUsername.AutoSize = true;
            this._lblAuthUsername.Location = new System.Drawing.Point(10, 46);
            this._lblAuthUsername.Name = "_lblAuthUsername";
            this._lblAuthUsername.TabIndex = 2;
            this._lblAuthUsername.Text = "Username:";
            this._lblAuthUsername.Visible = false;
            //
            // _txtAuthUsername
            //
            this._txtAuthUsername.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this._txtAuthUsername.Location = new System.Drawing.Point(80, 43);
            this._txtAuthUsername.Name = "_txtAuthUsername";
            this._txtAuthUsername.Size = new System.Drawing.Size(388, 20);
            this._txtAuthUsername.TabIndex = 3;
            this._txtAuthUsername.Visible = false;
            this._txtAuthUsername.TextChanged += new System.EventHandler(this.OnUserChange);
            //
            // _lblAuthPassword
            //
            this._lblAuthPassword.AutoSize = true;
            this._lblAuthPassword.Location = new System.Drawing.Point(10, 74);
            this._lblAuthPassword.Name = "_lblAuthPassword";
            this._lblAuthPassword.TabIndex = 4;
            this._lblAuthPassword.Text = "Password:";
            this._lblAuthPassword.Visible = false;
            //
            // _txtAuthPassword
            //
            this._txtAuthPassword.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this._txtAuthPassword.Location = new System.Drawing.Point(80, 71);
            this._txtAuthPassword.Name = "_txtAuthPassword";
            this._txtAuthPassword.Size = new System.Drawing.Size(388, 20);
            this._txtAuthPassword.TabIndex = 5;
            this._txtAuthPassword.UseSystemPasswordChar = true;
            this._txtAuthPassword.Visible = false;
            this._txtAuthPassword.TextChanged += new System.EventHandler(this.OnUserChange);
            //
            // _lblAuthToken
            //
            this._lblAuthToken.AutoSize = true;
            this._lblAuthToken.Location = new System.Drawing.Point(10, 46);
            this._lblAuthToken.Name = "_lblAuthToken";
            this._lblAuthToken.TabIndex = 6;
            this._lblAuthToken.Text = "Token:";
            this._lblAuthToken.Visible = false;
            //
            // _txtAuthToken
            //
            this._txtAuthToken.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this._txtAuthToken.Location = new System.Drawing.Point(80, 43);
            this._txtAuthToken.Name = "_txtAuthToken";
            this._txtAuthToken.Size = new System.Drawing.Size(388, 20);
            this._txtAuthToken.TabIndex = 7;
            this._txtAuthToken.UseSystemPasswordChar = true;
            this._txtAuthToken.Visible = false;
            this._txtAuthToken.TextChanged += new System.EventHandler(this.OnUserChange);
            // ── Tab: Options ──
            //
            // _tabOptions
            //
            this._tabOptions.Controls.Add(this._labelTimeout);
            this._tabOptions.Controls.Add(this._txtTimeout);
            this._tabOptions.Controls.Add(this._lblTimeoutUnit);
            this._tabOptions.Controls.Add(this._chkSkipCertValidation);
            this._tabOptions.Location = new System.Drawing.Point(4, 22);
            this._tabOptions.Name = "_tabOptions";
            this._tabOptions.Padding = new System.Windows.Forms.Padding(6);
            this._tabOptions.Size = new System.Drawing.Size(482, 154);
            this._tabOptions.TabIndex = 3;
            this._tabOptions.Text = "Options";
            this._tabOptions.UseVisualStyleBackColor = true;
            //
            // _labelTimeout
            //
            this._labelTimeout.AutoSize = true;
            this._labelTimeout.Location = new System.Drawing.Point(10, 14);
            this._labelTimeout.Name = "_labelTimeout";
            this._labelTimeout.TabIndex = 0;
            this._labelTimeout.Text = "Timeout:";
            //
            // _txtTimeout
            //
            this._txtTimeout.Location = new System.Drawing.Point(80, 11);
            this._txtTimeout.Name = "_txtTimeout";
            this._txtTimeout.Size = new System.Drawing.Size(70, 20);
            this._txtTimeout.TabIndex = 1;
            this._txtTimeout.TextChanged += new System.EventHandler(this.OnUserChange);
            //
            // _lblTimeoutUnit
            //
            this._lblTimeoutUnit.AutoSize = true;
            this._lblTimeoutUnit.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblTimeoutUnit.Location = new System.Drawing.Point(154, 14);
            this._lblTimeoutUnit.Name = "_lblTimeoutUnit";
            this._lblTimeoutUnit.TabIndex = 2;
            this._lblTimeoutUnit.Text = "ms";
            //
            // _chkSkipCertValidation
            //
            this._chkSkipCertValidation.AutoSize = true;
            this._chkSkipCertValidation.Location = new System.Drawing.Point(10, 42);
            this._chkSkipCertValidation.Name = "_chkSkipCertValidation";
            this._chkSkipCertValidation.Size = new System.Drawing.Size(231, 17);
            this._chkSkipCertValidation.TabIndex = 3;
            this._chkSkipCertValidation.Text = "Skip HTTPS certificate validation (self-signed)";
            this._chkSkipCertValidation.UseVisualStyleBackColor = true;
            this._chkSkipCertValidation.CheckedChanged += new System.EventHandler(this.OnUserChange);
            // ════════════════════════════════════════════════
            // RIGHT COLUMN - Test (x=510, width=278)
            // ════════════════════════════════════════════════
            //
            // _btnTest
            //
            this._btnTest.Location = new System.Drawing.Point(510, 38);
            this._btnTest.Name = "_btnTest";
            this._btnTest.Size = new System.Drawing.Size(278, 28);
            this._btnTest.TabIndex = 4;
            this._btnTest.Text = "Send Test Request";
            this._btnTest.UseVisualStyleBackColor = true;
            this._btnTest.Click += new System.EventHandler(this.OnTestClick);
            //
            // _grpTestResult
            //
            this._grpTestResult.Controls.Add(this._lblTestStatus);
            this._grpTestResult.Controls.Add(this._txtTestResponse);
            this._grpTestResult.Location = new System.Drawing.Point(510, 72);
            this._grpTestResult.Name = "_grpTestResult";
            this._grpTestResult.Size = new System.Drawing.Size(278, 446);
            this._grpTestResult.TabIndex = 5;
            this._grpTestResult.TabStop = false;
            this._grpTestResult.Text = "Test Result";
            //
            // _lblTestStatus
            //
            this._lblTestStatus.AutoSize = true;
            this._lblTestStatus.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this._lblTestStatus.Location = new System.Drawing.Point(8, 20);
            this._lblTestStatus.MaximumSize = new System.Drawing.Size(260, 0);
            this._lblTestStatus.Name = "_lblTestStatus";
            this._lblTestStatus.TabIndex = 0;
            this._lblTestStatus.Text = "";
            //
            // _txtTestResponse
            //
            this._txtTestResponse.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this._txtTestResponse.BackColor = System.Drawing.SystemColors.Window;
            this._txtTestResponse.Font = new System.Drawing.Font("Consolas", 8.25F);
            this._txtTestResponse.Location = new System.Drawing.Point(8, 40);
            this._txtTestResponse.Multiline = true;
            this._txtTestResponse.Name = "_txtTestResponse";
            this._txtTestResponse.ReadOnly = true;
            this._txtTestResponse.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this._txtTestResponse.Size = new System.Drawing.Size(262, 396);
            this._txtTestResponse.TabIndex = 1;
            this._txtTestResponse.WordWrap = false;
            //
            // HttpRequestUserControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.Controls.Add(this._labelTitle);
            this.Controls.Add(this._btnDuplicate);
            this.Controls.Add(this._grpRequest);
            this.Controls.Add(this._grpBody);
            this.Controls.Add(this._tabControl);
            this.Controls.Add(this._btnTest);
            this.Controls.Add(this._grpTestResult);
            this.MaximumSize = new System.Drawing.Size(800, 0);
            this.Name = "HttpRequestUserControl";
            this.Size = new System.Drawing.Size(800, 530);
            this._grpRequest.ResumeLayout(false);
            this._grpRequest.PerformLayout();
            this._grpBody.ResumeLayout(false);
            this._grpBody.PerformLayout();
            this._tabControl.ResumeLayout(false);
            this._tabUrlParams.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this._dgvQueryParams)).EndInit();
            this._tabHeaders.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this._dgvHeaders)).EndInit();
            this._tabAuth.ResumeLayout(false);
            this._tabAuth.PerformLayout();
            this._tabOptions.ResumeLayout(false);
            this._tabOptions.PerformLayout();
            this._grpTestResult.ResumeLayout(false);
            this._grpTestResult.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label _labelTitle;
        private System.Windows.Forms.Button _btnDuplicate;
        private System.Windows.Forms.GroupBox _grpRequest;
        private System.Windows.Forms.Label _labelName;
        private System.Windows.Forms.TextBox _txtName;
        private System.Windows.Forms.Label _labelMethod;
        private System.Windows.Forms.ComboBox _cboMethod;
        private System.Windows.Forms.Label _labelUrl;
        private System.Windows.Forms.TextBox _txtUrl;
        private System.Windows.Forms.CheckBox _chkEnabled;
        private System.Windows.Forms.GroupBox _grpBody;
        private System.Windows.Forms.Label _labelPayloadType;
        private System.Windows.Forms.ComboBox _cboPayloadType;
        private FastColoredTextBoxNS.FastColoredTextBox _txtPayload;
        private System.Windows.Forms.Label _lblJsonHint;
        private System.Windows.Forms.CheckBox _chkIncludeEventData;
        private System.Windows.Forms.TabControl _tabControl;
        private System.Windows.Forms.TabPage _tabUrlParams;
        private System.Windows.Forms.DataGridView _dgvQueryParams;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colQpKey;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colQpValue;
        private System.Windows.Forms.Button _btnRemoveQueryParam;
        private System.Windows.Forms.TabPage _tabHeaders;
        private System.Windows.Forms.DataGridView _dgvHeaders;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colHdrKey;
        private System.Windows.Forms.DataGridViewTextBoxColumn _colHdrValue;
        private System.Windows.Forms.Button _btnRemoveHeader;
        private System.Windows.Forms.TabPage _tabAuth;
        private System.Windows.Forms.Label _labelAuthType;
        private System.Windows.Forms.ComboBox _cboAuthType;
        private System.Windows.Forms.Label _lblAuthUsername;
        private System.Windows.Forms.TextBox _txtAuthUsername;
        private System.Windows.Forms.Label _lblAuthPassword;
        private System.Windows.Forms.TextBox _txtAuthPassword;
        private System.Windows.Forms.Label _lblAuthToken;
        private System.Windows.Forms.TextBox _txtAuthToken;
        private System.Windows.Forms.TabPage _tabOptions;
        private System.Windows.Forms.Label _labelTimeout;
        private System.Windows.Forms.TextBox _txtTimeout;
        private System.Windows.Forms.Label _lblTimeoutUnit;
        private System.Windows.Forms.CheckBox _chkSkipCertValidation;
        private System.Windows.Forms.Button _btnTest;
        private System.Windows.Forms.GroupBox _grpTestResult;
        private System.Windows.Forms.Label _lblTestStatus;
        private System.Windows.Forms.TextBox _txtTestResponse;
    }
}
