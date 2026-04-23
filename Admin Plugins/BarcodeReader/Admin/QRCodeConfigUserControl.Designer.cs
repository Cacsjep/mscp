namespace BarcodeReader.Admin
{
    partial class QRCodeConfigUserControl
    {
        private System.ComponentModel.IContainer components = null;

        private void InitializeComponent()
        {
            this._labelTitle = new System.Windows.Forms.Label();
            this._labelName = new System.Windows.Forms.Label();
            this._txtName = new System.Windows.Forms.TextBox();

            this._labelPayload = new System.Windows.Forms.Label();
            this._txtPayload = new System.Windows.Forms.TextBox();
            this._lblPayloadHint = new System.Windows.Forms.Label();

            this._labelEC = new System.Windows.Forms.Label();
            this._cboEC = new System.Windows.Forms.ComboBox();
            this._lblECHint = new System.Windows.Forms.Label();

            this._grpPreview = new System.Windows.Forms.GroupBox();
            this._pbPreview = new System.Windows.Forms.PictureBox();
            this._btnCopyPng = new System.Windows.Forms.Button();
            this._btnSavePng = new System.Windows.Forms.Button();

            this._grpPreview.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._pbPreview)).BeginInit();
            this.SuspendLayout();

            // All form controls are fixed-width on the left - no right-anchoring anywhere
            // on this panel. Preview sits below the Error-correction hint, inline with the
            // rest of the form, so everything reads top to bottom without horizontal spread.
            const int FormFieldWidth = 540;
            const int PreviewWidth = 280;
            const int PreviewHeight = 340;

            //
            // Title
            //
            this._labelTitle.AutoSize = true;
            this._labelTitle.Font = new System.Drawing.Font("Segoe UI", 13F, System.Drawing.FontStyle.Bold);
            this._labelTitle.Location = new System.Drawing.Point(16, 14);
            this._labelTitle.Text = "QR Code";

            //
            // Name
            //
            this._labelName.AutoSize = true;
            this._labelName.Location = new System.Drawing.Point(18, 54);
            this._labelName.Text = "Name:";

            this._txtName.Location = new System.Drawing.Point(130, 51);
            this._txtName.Size = new System.Drawing.Size(FormFieldWidth, 23);
            this._txtName.TabIndex = 0;
            this._txtName.TextChanged += new System.EventHandler(this.OnUserChange);

            //
            // Payload
            //
            this._labelPayload.AutoSize = true;
            this._labelPayload.Location = new System.Drawing.Point(18, 84);
            this._labelPayload.Text = "Payload:";

            this._txtPayload.Location = new System.Drawing.Point(130, 81);
            this._txtPayload.Size = new System.Drawing.Size(FormFieldWidth, 23);
            this._txtPayload.TabIndex = 1;
            this._txtPayload.TextChanged += new System.EventHandler(this.OnUserChange);

            this._lblPayloadHint.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblPayloadHint.Location = new System.Drawing.Point(130, 106);
            this._lblPayloadHint.Size = new System.Drawing.Size(FormFieldWidth, 18);
            this._lblPayloadHint.Text = "Exact text a scanner must decode to fire the 'QR Code Matched' event. Must be unique across QR Code items.";

            //
            // Error correction
            //
            this._labelEC.AutoSize = true;
            this._labelEC.Location = new System.Drawing.Point(18, 134);
            this._labelEC.Text = "Error correction:";

            this._cboEC.Location = new System.Drawing.Point(130, 131);
            this._cboEC.Size = new System.Drawing.Size(240, 23);
            this._cboEC.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._cboEC.SelectedIndexChanged += new System.EventHandler(this.OnUserChange);

            this._lblECHint.ForeColor = System.Drawing.SystemColors.GrayText;
            this._lblECHint.Location = new System.Drawing.Point(130, 156);
            this._lblECHint.Size = new System.Drawing.Size(FormFieldWidth, 18);
            this._lblECHint.Text = "Higher levels recover from more damage/dirt but produce denser codes.";

            //
            // Preview group sits directly below the Error-correction hint (Top+Left only).
            //
            this._grpPreview.Location = new System.Drawing.Point(130, 190);
            this._grpPreview.Size = new System.Drawing.Size(PreviewWidth, PreviewHeight);
            this._grpPreview.Text = "Preview";
            this._grpPreview.Controls.Add(this._pbPreview);
            this._grpPreview.Controls.Add(this._btnCopyPng);
            this._grpPreview.Controls.Add(this._btnSavePng);

            this._pbPreview.Location = new System.Drawing.Point(12, 28);
            this._pbPreview.Size = new System.Drawing.Size(256, 256);
            this._pbPreview.BackColor = System.Drawing.Color.White;
            this._pbPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._pbPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this._pbPreview.TabStop = false;

            this._btnCopyPng.Location = new System.Drawing.Point(12, 298);
            this._btnCopyPng.Size = new System.Drawing.Size(120, 28);
            this._btnCopyPng.Text = "Copy PNG";
            this._btnCopyPng.UseVisualStyleBackColor = true;
            this._btnCopyPng.Click += new System.EventHandler(this.BtnCopyPng_Click);

            this._btnSavePng.Location = new System.Drawing.Point(148, 298);
            this._btnSavePng.Size = new System.Drawing.Size(120, 28);
            this._btnSavePng.Text = "Save PNG...";
            this._btnSavePng.UseVisualStyleBackColor = true;
            this._btnSavePng.Click += new System.EventHandler(this.BtnSavePng_Click);

            //
            // Form
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.Controls.Add(this._labelTitle);
            this.Controls.Add(this._labelName);
            this.Controls.Add(this._txtName);
            this.Controls.Add(this._labelPayload);
            this.Controls.Add(this._txtPayload);
            this.Controls.Add(this._lblPayloadHint);
            this.Controls.Add(this._labelEC);
            this.Controls.Add(this._cboEC);
            this.Controls.Add(this._lblECHint);
            this.Controls.Add(this._grpPreview);
            this.Name = "QRCodeConfigUserControl";
            this.Size = new System.Drawing.Size(720, 560);

            this._grpPreview.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this._pbPreview)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label _labelTitle;
        private System.Windows.Forms.Label _labelName;
        private System.Windows.Forms.TextBox _txtName;
        private System.Windows.Forms.Label _labelPayload;
        private System.Windows.Forms.TextBox _txtPayload;
        private System.Windows.Forms.Label _lblPayloadHint;
        private System.Windows.Forms.Label _labelEC;
        private System.Windows.Forms.ComboBox _cboEC;
        private System.Windows.Forms.Label _lblECHint;
        private System.Windows.Forms.GroupBox _grpPreview;
        private System.Windows.Forms.PictureBox _pbPreview;
        private System.Windows.Forms.Button _btnCopyPng;
        private System.Windows.Forms.Button _btnSavePng;
    }
}
