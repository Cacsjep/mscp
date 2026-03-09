using System;
using System.Windows.Forms;
using VideoOS.Platform;

namespace HttpRequests.Admin
{
    public partial class HttpFolderUserControl : UserControl
    {
        internal event EventHandler ConfigurationChangedByUser;

        public HttpFolderUserControl()
        {
            InitializeComponent();
        }

        public string DisplayName => _txtName.Text;

        public void FillContent(Item item)
        {
            if (item == null)
            {
                ClearContent();
                return;
            }
            _txtName.Text = item.Name;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;
            item.Name = _txtName.Text;
        }

        public void ClearContent()
        {
            _txtName.Text = "";
        }

        private void OnUserChange(object sender, EventArgs e)
        {
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }
    }
}
