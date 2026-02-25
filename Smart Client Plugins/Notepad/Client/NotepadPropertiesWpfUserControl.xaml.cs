using VideoOS.Platform.Client;

namespace Notepad.Client
{
    public partial class NotepadPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly NotepadViewItemManager _viewItemManager;

        public NotepadPropertiesWpfUserControl(NotepadViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            titleTextBox.Text = _viewItemManager.Title;
            fontSizeTextBox.Text = _viewItemManager.FontSize;
        }

        public override void Close()
        {
            _viewItemManager.Title = titleTextBox.Text.Trim();

            var fontSizeText = fontSizeTextBox.Text.Trim();
            if (double.TryParse(fontSizeText, out var size) && size > 0 && size <= 72)
                _viewItemManager.FontSize = fontSizeText;

            _viewItemManager.Save();
        }
    }
}
