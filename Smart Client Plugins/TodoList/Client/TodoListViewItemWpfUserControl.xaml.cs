using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TodoList.Client.Models;
using TodoList.Client.Storage;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace TodoList.Client
{
    public partial class TodoListViewItemWpfUserControl : ViewItemWpfUserControl
    {
        private readonly TodoListViewItemManager _viewItemManager;
        private object _modeChangedReceiver;
        private volatile bool _closing;
        private bool _refreshQueued;

        private readonly ObservableCollection<TodoTask> _freeTasks = new ObservableCollection<TodoTask>();
        private readonly ObservableCollection<TodoTask> _templateRows = new ObservableCollection<TodoTask>();

        private TodoTemplate _activeTemplate;

        public TodoListViewItemWpfUserControl(TodoListViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();

            freeTasksList.ItemsSource = _freeTasks;
            templateTasksList.ItemsSource = _templateRows;
        }

        public override void Init()
        {
            _closing = false;

            if (_modeChangedReceiver == null)
            {
                _modeChangedReceiver = EnvironmentManager.Instance.RegisterReceiver(
                    new MessageReceiver(OnModeChanged),
                    new MessageIdFilter(MessageId.System.ModeChangedIndication));
            }

            // Defensive: remove before add so re-entry can't double-subscribe.
            _viewItemManager.ConfigurationChanged -= OnConfigurationChanged;
            _viewItemManager.ConfigurationChanged += OnConfigurationChanged;

            ApplyMode(EnvironmentManager.Instance.Mode);
        }

        public override void Close()
        {
            _closing = true;

            if (_modeChangedReceiver != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_modeChangedReceiver);
                _modeChangedReceiver = null;
            }

            _viewItemManager.ConfigurationChanged -= OnConfigurationChanged;

            // Clear active shift on view close - next session starts with picker.
            // Only write if there is actually something to clear; Smart Client may
            // create/destroy this control on every view switch, and a no-op
            // SaveProperties() round-trip on each one shows up as UI lag.
            if (!string.IsNullOrEmpty(_viewItemManager.ActiveTemplateId))
            {
                _viewItemManager.ActiveTemplateId = string.Empty;
                _viewItemManager.SetTemplateProgress(new Dictionary<string, bool>());
                _viewItemManager.Save();
            }
        }

        // ----- Mode handling -----

        private void ApplyMode(Mode mode)
        {
            UpdateTitle();
            UpdateModeChip();
            ApplyFontSize();

            if (mode == Mode.ClientSetup)
            {
                HideAllLivePanels();
                setupOverlay.Visibility = Visibility.Visible;
                UpdateSetupInfo();
            }
            else
            {
                setupOverlay.Visibility = Visibility.Collapsed;
                ShowActiveLivePanel();
            }
        }

        private void HideAllLivePanels()
        {
            freePanel.Visibility = Visibility.Collapsed;
            templateNoSelectionPanel.Visibility = Visibility.Collapsed;
            templateRunPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowActiveLivePanel()
        {
            HideAllLivePanels();

            if (_viewItemManager.Mode == TodoListViewItemManager.ModeTemplate)
            {
                LoadActiveTemplate();
                if (_activeTemplate == null)
                {
                    templateNoSelectionPanel.Visibility = Visibility.Visible;
                    UpdateTemplateNoSelectionText();
                }
                else
                {
                    templateRunPanel.Visibility = Visibility.Visible;
                }
            }
            else
            {
                LoadFreeTasks();
                freePanel.Visibility = Visibility.Visible;
            }

            UpdateProgressText();
        }

        private object OnModeChanged(Message message, FQID destination, FQID sender)
        {
            QueueRefresh();
            return null;
        }

        private void OnConfigurationChanged(object sender, EventArgs e)
        {
            QueueRefresh();
        }

        // Coalesce mode-changed + config-changed signals so back-to-back events
        // (e.g. Properties.Close raises ConfigurationChanged then Smart Client
        // fires ModeChangedIndication for Live) only trigger one refresh.
        private void QueueRefresh()
        {
            if (_closing || _refreshQueued)
                return;
            _refreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _refreshQueued = false;
                if (_closing) return;
                ApplyMode(EnvironmentManager.Instance.Mode);
            }));
        }

        // ----- Free mode -----

        private void LoadFreeTasks()
        {
            _freeTasks.Clear();
            foreach (var t in _viewItemManager.GetFreeTasks())
                _freeTasks.Add(t);
        }

        private void PersistFreeTasks()
        {
            _viewItemManager.SetFreeTasks(_freeTasks.ToList());
            _viewItemManager.Save();
            UpdateProgressText();
        }

        private void OnAddFreeTaskClick(object sender, RoutedEventArgs e)
        {
            AddFreeTaskFromInput();
        }

        private void OnNewTaskKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddFreeTaskFromInput();
                e.Handled = true;
            }
        }

        private void AddFreeTaskFromInput()
        {
            var text = newTaskTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            _freeTasks.Add(new TodoTask
            {
                Id = TodoStorage.NewId(),
                Text = text,
                Done = false
            });
            newTaskTextBox.Clear();
            PersistFreeTasks();
            newTaskTextBox.Focus();
        }

        private void OnFreeTaskCheckClick(object sender, RoutedEventArgs e)
        {
            PersistFreeTasks();
        }

        private void OnFreeTaskMoveUpClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is TodoTask task))
                return;
            var idx = _freeTasks.IndexOf(task);
            if (idx > 0)
            {
                _freeTasks.Move(idx, idx - 1);
                PersistFreeTasks();
            }
        }

        private void OnFreeTaskMoveDownClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is TodoTask task))
                return;
            var idx = _freeTasks.IndexOf(task);
            if (idx >= 0 && idx < _freeTasks.Count - 1)
            {
                _freeTasks.Move(idx, idx + 1);
                PersistFreeTasks();
            }
        }

        private void OnFreeTaskDeleteClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is TodoTask task))
                return;
            _freeTasks.Remove(task);
            PersistFreeTasks();
        }

        private void OnClearCompletedClick(object sender, RoutedEventArgs e)
        {
            var keep = _freeTasks.Where(t => !t.Done).ToList();
            _freeTasks.Clear();
            foreach (var t in keep)
                _freeTasks.Add(t);
            PersistFreeTasks();
        }

        private void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            if (_freeTasks.Count == 0)
                return;

            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Remove all tasks from this list?",
                "Clear all",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.OK)
                return;

            _freeTasks.Clear();
            PersistFreeTasks();
        }

        // ----- Template mode -----

        private void LoadActiveTemplate()
        {
            _activeTemplate = _viewItemManager.GetActiveTemplate();
            _templateRows.Clear();

            if (_activeTemplate == null)
                return;

            var progress = _viewItemManager.GetTemplateProgress();

            // Reconcile: drop progress entries for tasks no longer in the template.
            var validIds = new HashSet<string>(_activeTemplate.Tasks.Select(t => t.Id));
            var changed = false;
            foreach (var key in progress.Keys.ToList())
            {
                if (!validIds.Contains(key))
                {
                    progress.Remove(key);
                    changed = true;
                }
            }
            if (changed)
            {
                _viewItemManager.SetTemplateProgress(progress);
                _viewItemManager.Save();
            }

            foreach (var t in _activeTemplate.Tasks)
            {
                _templateRows.Add(new TodoTask
                {
                    Id = t.Id,
                    Text = t.Text,
                    Done = progress.TryGetValue(t.Id, out var done) && done
                });
            }
        }

        private void OnTemplateTaskCheckClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.DataContext is TodoTask row))
                return;

            var progress = _viewItemManager.GetTemplateProgress();
            if (row.Done)
                progress[row.Id] = true;
            else
                progress.Remove(row.Id);
            _viewItemManager.SetTemplateProgress(progress);
            _viewItemManager.Save();
            UpdateProgressText();
        }

        private void OnPickTemplateClick(object sender, RoutedEventArgs e)
        {
            PickAndActivateTemplate(preselectActive: false);
        }

        private void OnSwitchTemplateClick(object sender, RoutedEventArgs e)
        {
            PickAndActivateTemplate(preselectActive: true);
        }

        private void OnStartNewShiftClick(object sender, RoutedEventArgs e)
        {
            if (_activeTemplate == null)
                return;

            var result = MessageBox.Show(
                Window.GetWindow(this),
                "Start a new shift? All checks will be cleared.",
                "Start new shift",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.OK)
                return;

            _viewItemManager.SetTemplateProgress(new Dictionary<string, bool>());
            _viewItemManager.Save();
            LoadActiveTemplate();
            UpdateProgressText();
        }

        private void PickAndActivateTemplate(bool preselectActive)
        {
            var templates = _viewItemManager.GetTemplates();
            if (templates.Count == 0)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "No templates defined yet. Open the Properties panel and click 'Manage templates...' to add one.",
                    "No templates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string preselectId;
            if (preselectActive && _activeTemplate != null)
                preselectId = _activeTemplate.Id;
            else
                preselectId = _viewItemManager.DefaultTemplateId;

            var dlg = new TodoListTemplatePickerWindow(templates, preselectId);
            try
            {
                var owner = Window.GetWindow(this);
                if (owner != null)
                    dlg.Owner = owner;
            }
            catch { }

            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedTemplateId))
            {
                ActivateTemplate(dlg.SelectedTemplateId);
            }
        }

        private void ActivateTemplate(string templateId)
        {
            var changingTemplate = _viewItemManager.ActiveTemplateId != templateId;
            _viewItemManager.ActiveTemplateId = templateId ?? string.Empty;
            if (changingTemplate)
                _viewItemManager.SetTemplateProgress(new Dictionary<string, bool>());
            _viewItemManager.Save();

            ShowActiveLivePanel();
        }

        // ----- Title bar / chrome -----

        private void UpdateTitle()
        {
            var title = _viewItemManager.Title;
            titleText.Text = string.IsNullOrWhiteSpace(title) ? "Todo List" : title;
        }

        private void UpdateModeChip()
        {
            modeChipText.Text = _viewItemManager.Mode == TodoListViewItemManager.ModeTemplate
                ? "Template"
                : "Free";
        }

        private void UpdateProgressText()
        {
            if (_viewItemManager.Mode == TodoListViewItemManager.ModeTemplate)
            {
                if (_activeTemplate == null)
                {
                    progressText.Text = string.Empty;
                    return;
                }
                var progress = _viewItemManager.GetTemplateProgress();
                var total = _activeTemplate.Tasks.Count;
                var done = _activeTemplate.Tasks.Count(t => progress.TryGetValue(t.Id, out var v) && v);
                progressText.Text = $"{_activeTemplate.Name} - {done} of {total} done";
            }
            else
            {
                if (_freeTasks.Count == 0)
                {
                    progressText.Text = string.Empty;
                    return;
                }
                var done = _freeTasks.Count(t => t.Done);
                progressText.Text = $"{done} of {_freeTasks.Count} done";
            }
        }

        private void UpdateTemplateNoSelectionText()
        {
            var def = _viewItemManager.GetDefaultTemplate();
            templateNoSelectionText.Text = def != null
                ? $"No template active. Default: {def.Name}"
                : "No template active.";
        }

        private void UpdateSetupInfo()
        {
            var title = _viewItemManager.Title;
            setupTitleText.Text = string.IsNullOrWhiteSpace(title) ? "Todo List" : title;

            var mode = _viewItemManager.Mode == TodoListViewItemManager.ModeTemplate
                ? "Template"
                : "Free";
            var defaultTemplate = _viewItemManager.GetDefaultTemplate();
            var templateInfo = defaultTemplate != null
                ? $" - Default template: {defaultTemplate.Name}"
                : string.Empty;
            setupInfoText.Text = $"Mode: {mode}{templateInfo}";
        }

        private void ApplyFontSize()
        {
            if (double.TryParse(_viewItemManager.FontSize, out var size) && size > 0 && size <= 72)
                FontSize = size;
            else
                FontSize = 14;
        }

        // ----- View item plumbing -----

        private void OnMouseLeftUp(object sender, MouseButtonEventArgs e) => FireClickEvent();
        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => FireDoubleClickEvent();

        public override bool Maximizable => true;
        public override bool Selectable => true;
        public override bool ShowToolbar => false;
    }
}
