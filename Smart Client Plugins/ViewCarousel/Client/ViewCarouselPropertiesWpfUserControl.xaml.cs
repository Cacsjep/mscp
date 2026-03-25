using System.Windows;
using VideoOS.Platform.Client;

namespace ViewCarousel.Client
{
    public partial class ViewCarouselPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly ViewCarouselViewItemManager _viewItemManager;

        public ViewCarouselPropertiesWpfUserControl(ViewCarouselViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            UpdateSummary();
        }

        public override void Close()
        {
        }

        private void UpdateSummary()
        {
            var entries = _viewItemManager.GetViewEntryList();
            var defaultTime = _viewItemManager.DefaultTime;
            if (entries.Count == 0)
                lblSummary.Text = "Not configured";
            else
                lblSummary.Text = $"{entries.Count} view(s), {defaultTime} sec default";
        }

        private void OnCarouselSetupClick(object sender, RoutedEventArgs e)
        {
            var entries = _viewItemManager.GetViewEntryList();
            int.TryParse(_viewItemManager.DefaultTime, out int defaultTime);
            if (defaultTime <= 0) defaultTime = 10;

            var dialog = new ViewCarouselSetupWindow(entries, defaultTime);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                _viewItemManager.SetViewEntryList(dialog.ResultEntries);
                _viewItemManager.DefaultTime = dialog.ResultDefaultTime.ToString();
                _viewItemManager.Save();
                UpdateSummary();
            }
        }
    }
}
