namespace HttpRequests.Admin
{
    partial class HttpFolderUserControl
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
            this._labelName = new System.Windows.Forms.Label();
            this._txtName = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // _labelTitle
            // 
            this._labelTitle.AutoSize = true;
            this._labelTitle.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this._labelTitle.Location = new System.Drawing.Point(12, 12);
            this._labelTitle.Name = "_labelTitle";
            this._labelTitle.Size = new System.Drawing.Size(123, 21);
            this._labelTitle.TabIndex = 0;
            this._labelTitle.Text = "Request Folder";
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
            // HttpFolderUserControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._labelTitle);
            this.Controls.Add(this._labelName);
            this.Controls.Add(this._txtName);
            this.Name = "HttpFolderUserControl";
            this.Size = new System.Drawing.Size(530, 200);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Label _labelTitle;
        private System.Windows.Forms.Label _labelName;
        private System.Windows.Forms.TextBox _txtName;
    }
}
