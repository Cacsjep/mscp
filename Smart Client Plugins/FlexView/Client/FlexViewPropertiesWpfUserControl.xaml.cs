using System.Windows.Media;
using VideoOS.Platform.Client;

namespace FlexView.Client
{
    public partial class FlexViewPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly FlexViewViewItemManager _viewItemManager;

        public FlexViewPropertiesWpfUserControl(FlexViewViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            backgroundColorTextBox.Text = _viewItemManager.BackgroundColor;
        }

        public override void Close()
        {
            var value = (backgroundColorTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                value = "#FF070809";

            try
            {
                ColorConverter.ConvertFromString(value);
                _viewItemManager.BackgroundColor = value;
            }
            catch
            {
                _viewItemManager.BackgroundColor = "#FF070809";
            }

            _viewItemManager.Save();
        }
    }
}
