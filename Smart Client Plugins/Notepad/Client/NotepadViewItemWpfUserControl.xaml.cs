using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace Notepad.Client
{
    public partial class NotepadViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly NotepadViewItemManager _viewItemManager;
        private object _modeChangedReceiver;
        private DispatcherTimer _autoSaveTimer;
        private bool _isDirty;
        private bool _suppressTextChanged;

        public NotepadViewItemWpfUserControl(NotepadViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                new MessageReceiver(OnModeChanged),
                new MessageIdFilter(MessageId.System.ModeChangedIndication));

            noteTextBox.TextChanged += NoteTextBox_TextChanged;

            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(30);
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;

            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }

            _autoSaveTimer?.Stop();
            SaveNoteContent();
        }

        private void ApplyMode(Mode mode)
        {
            if (mode == Mode.ClientSetup)
            {
                SaveNoteContent();
                _autoSaveTimer?.Stop();

                noteTextBox.Visibility = Visibility.Collapsed;
                setupOverlay.Visibility = Visibility.Visible;
                UpdateSetupInfo();
            }
            else
            {
                setupOverlay.Visibility = Visibility.Collapsed;
                noteTextBox.Visibility = Visibility.Visible;

                LoadNoteContent();
                ApplyFontSize();
                UpdateTitle();

                _autoSaveTimer?.Start();
            }
        }

        private void LoadNoteContent()
        {
            _suppressTextChanged = true;
            noteTextBox.Text = _viewItemManager.NoteContent;
            _suppressTextChanged = false;
            _isDirty = false;
            savedIndicator.Text = "Saved";
            saveButton.Visibility = Visibility.Collapsed;
        }

        private void SaveNoteContent()
        {
            if (!_isDirty)
                return;

            _viewItemManager.NoteContent = noteTextBox.Text;
            _viewItemManager.Save();
            _isDirty = false;
            savedIndicator.Text = "Saved";
            saveButton.Visibility = Visibility.Collapsed;
        }

        private void NoteTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressTextChanged)
                return;

            _isDirty = true;
            savedIndicator.Text = "";
            saveButton.Visibility = Visibility.Visible;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveNoteContent();
            saveButton.Visibility = Visibility.Collapsed;
        }

        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            SaveNoteContent();
            saveButton.Visibility = Visibility.Collapsed;
        }

        private void ApplyFontSize()
        {
            if (double.TryParse(_viewItemManager.FontSize, out var size) && size > 0)
                noteTextBox.FontSize = size;
            else
                noteTextBox.FontSize = 14;
        }

        private void UpdateTitle()
        {
            var title = _viewItemManager.Title;
            titleText.Text = string.IsNullOrWhiteSpace(title) ? "Notepad" : title;
        }

        private void UpdateSetupInfo()
        {
            var title = _viewItemManager.Title;
            setupTitleText.Text = string.IsNullOrWhiteSpace(title) ? "Notepad" : title;
            setupInfoText.Text = $"Font size: {_viewItemManager.FontSize}";
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyMode((Mode)message.Data);
            }));
            return null;
        }

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            FireClickEvent();
        }

        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            FireDoubleClickEvent();
        }

        public override bool Maximizable => true;

        public override bool Selectable => true;

        public override bool ShowToolbar => false;
    }
}
