using VideoOS.Platform.Client;

namespace Notepad.Client
{
    public class NotepadViewItemManager : ViewItemManager
    {
        private const string NoteContentPropertyKey = "NoteContent";
        private const string TitlePropertyKey = "Title";
        private const string FontSizePropertyKey = "FontSize";

        public NotepadViewItemManager()
            : base("NotepadViewItemManager")
        {
        }

        public string NoteContent
        {
            get => GetProperty(NoteContentPropertyKey) ?? string.Empty;
            set => SetProperty(NoteContentPropertyKey, value);
        }

        public string Title
        {
            get => GetProperty(TitlePropertyKey) ?? string.Empty;
            set => SetProperty(TitlePropertyKey, value);
        }

        public string FontSize
        {
            get => GetProperty(FontSizePropertyKey) ?? "14";
            set => SetProperty(FontSizePropertyKey, value);
        }

        public void Save()
        {
            SaveProperties();
        }

        public override void PropertiesLoaded()
        {
        }

        public override ViewItemWpfUserControl GenerateViewItemWpfUserControl()
        {
            return new NotepadViewItemWpfUserControl(this);
        }

        public override PropertiesWpfUserControl GeneratePropertiesWpfUserControl()
        {
            return new NotepadPropertiesWpfUserControl(this);
        }
    }
}
