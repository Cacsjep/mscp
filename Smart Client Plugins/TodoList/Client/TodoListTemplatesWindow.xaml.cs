using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TodoList.Client.Models;
using TodoList.Client.Storage;

namespace TodoList.Client
{
    public partial class TodoListTemplatesWindow : Window
    {
        private readonly TodoListViewItemManager _viewItemManager;
        private readonly ObservableCollection<TemplateEdit> _templates = new ObservableCollection<TemplateEdit>();
        private readonly ObservableCollection<TaskEdit> _tasks = new ObservableCollection<TaskEdit>();
        private TemplateEdit _currentTemplate;
        private bool _dirty;

        public TodoListTemplatesWindow(TodoListViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();

            templatesListBox.ItemsSource = _templates;
            tasksListBox.ItemsSource = _tasks;

            LoadFromManager();
            UpdateEditorEnabled();
            UpdateTaskButtonsEnabled();
        }

        private void LoadFromManager()
        {
            _templates.Clear();
            foreach (var t in _viewItemManager.GetTemplates())
            {
                _templates.Add(new TemplateEdit
                {
                    Id = t.Id,
                    Name = t.Name,
                    Tasks = t.Tasks.Select(tt => new TaskEdit { Id = tt.Id, Text = tt.Text }).ToList()
                });
            }

            if (_templates.Count > 0)
                templatesListBox.SelectedIndex = 0;
        }

        // ----- Template selection -----

        private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentTemplate = templatesListBox.SelectedItem as TemplateEdit;
            RefreshTaskListFromCurrent();
            UpdateEditorEnabled();
            UpdateTaskButtonsEnabled();
        }

        private void RefreshTaskListFromCurrent()
        {
            _tasks.Clear();
            if (_currentTemplate == null)
            {
                editorTitleText.Text = "No template selected";
                return;
            }

            editorTitleText.Text = $"Editing: {(string.IsNullOrEmpty(_currentTemplate.Name) ? "(unnamed)" : _currentTemplate.Name)}";

            for (int i = 0; i < _currentTemplate.Tasks.Count; i++)
            {
                var t = _currentTemplate.Tasks[i];
                _tasks.Add(new TaskEdit
                {
                    Id = t.Id,
                    Text = t.Text,
                    DisplayNumber = (i + 1).ToString() + "."
                });
            }
        }

        private void UpdateEditorEnabled()
        {
            var hasTemplate = _currentTemplate != null;
            newTaskTextBox.IsEnabled = hasTemplate;
            addTaskButton.IsEnabled = hasTemplate;
            duplicateTemplateButton.IsEnabled = hasTemplate;
            renameTemplateButton.IsEnabled = hasTemplate;
            deleteTemplateButton.IsEnabled = hasTemplate;
        }

        private void UpdateTaskButtonsEnabled()
        {
            var task = tasksListBox.SelectedItem as TaskEdit;
            var hasTask = _currentTemplate != null && task != null;
            editTaskButton.IsEnabled = hasTask;
            deleteTaskButton.IsEnabled = hasTask;

            var idx = task != null ? _tasks.IndexOf(task) : -1;
            moveTaskUpButton.IsEnabled = hasTask && idx > 0;
            moveTaskDownButton.IsEnabled = hasTask && idx >= 0 && idx < _tasks.Count - 1;
        }

        // ----- Template CRUD -----

        private void OnNewTemplateClick(object sender, RoutedEventArgs e)
        {
            var name = TodoListTextInputDialog.Prompt(this, "New template", "Template name:", "New template");
            if (string.IsNullOrWhiteSpace(name))
                return;

            var t = new TemplateEdit
            {
                Id = TodoStorage.NewId(),
                Name = name.Trim(),
                Tasks = new List<TaskEdit>()
            };
            _templates.Add(t);
            templatesListBox.SelectedItem = t;
            _dirty = true;
        }

        private void OnDuplicateTemplateClick(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null)
                return;

            var copy = new TemplateEdit
            {
                Id = TodoStorage.NewId(),
                Name = $"Copy of {_currentTemplate.Name}",
                Tasks = _currentTemplate.Tasks
                    .Select(t => new TaskEdit { Id = TodoStorage.NewId(), Text = t.Text })
                    .ToList()
            };
            _templates.Add(copy);
            templatesListBox.SelectedItem = copy;
            _dirty = true;
        }

        private void OnRenameTemplateClick(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null)
                return;

            var name = TodoListTextInputDialog.Prompt(this, "Rename template", "Template name:", _currentTemplate.Name);
            if (name == null)
                return;

            ApplyTemplateNameChange(name.Trim());
        }

        private void ApplyTemplateNameChange(string newName)
        {
            if (_currentTemplate == null)
                return;
            if (_currentTemplate.Name == newName)
                return;

            _currentTemplate.Name = newName;

            // Refresh ListBox row text (Name binds via DisplayMemberPath; reseat the item).
            var idx = _templates.IndexOf(_currentTemplate);
            if (idx >= 0)
            {
                var keep = _currentTemplate;
                _templates.RemoveAt(idx);
                _templates.Insert(idx, keep);
                templatesListBox.SelectedItem = keep;
            }

            editorTitleText.Text = $"Editing: {(string.IsNullOrEmpty(newName) ? "(unnamed)" : newName)}";
            _dirty = true;
        }

        private void OnDeleteTemplateClick(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null)
                return;

            var msg = $"Delete template '{_currentTemplate.Name}'?";
            if (_viewItemManager.ActiveTemplateId == _currentTemplate.Id)
                msg += "\n\nThis template is currently active in the view. The shift will be cleared.";

            var result = MessageBox.Show(this, msg, "Delete template", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
                return;

            _templates.Remove(_currentTemplate);
            _currentTemplate = null;
            _dirty = true;

            if (_templates.Count > 0)
                templatesListBox.SelectedIndex = 0;
            else
                RefreshTaskListFromCurrent();
        }

        // ----- Task CRUD -----

        private void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTaskButtonsEnabled();
        }

        private void OnNewTaskKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddTaskFromInput();
                e.Handled = true;
            }
        }

        private void OnAddTaskClick(object sender, RoutedEventArgs e)
        {
            AddTaskFromInput();
        }

        private void AddTaskFromInput()
        {
            if (_currentTemplate == null)
                return;
            var text = newTaskTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            var task = new TaskEdit { Id = TodoStorage.NewId(), Text = text };
            _currentTemplate.Tasks.Add(task);
            newTaskTextBox.Clear();
            RefreshTaskListFromCurrent();
            _dirty = true;
            newTaskTextBox.Focus();
        }

        private void OnEditTaskClick(object sender, RoutedEventArgs e)
        {
            if (!(tasksListBox.SelectedItem is TaskEdit row) || _currentTemplate == null)
                return;

            var newText = TodoListTextInputDialog.Prompt(this, "Edit task", "Task text:", row.Text);
            if (newText == null)
                return;

            var inTemplate = _currentTemplate.Tasks.FirstOrDefault(t => t.Id == row.Id);
            if (inTemplate != null)
            {
                inTemplate.Text = newText.Trim();
                RefreshTaskListFromCurrent();
                _dirty = true;
            }
        }

        private void OnMoveTaskUpClick(object sender, RoutedEventArgs e)
        {
            MoveSelectedTask(-1);
        }

        private void OnMoveTaskDownClick(object sender, RoutedEventArgs e)
        {
            MoveSelectedTask(+1);
        }

        private void MoveSelectedTask(int delta)
        {
            if (_currentTemplate == null)
                return;
            if (!(tasksListBox.SelectedItem is TaskEdit row))
                return;

            var idx = _currentTemplate.Tasks.FindIndex(t => t.Id == row.Id);
            if (idx < 0)
                return;
            var target = idx + delta;
            if (target < 0 || target >= _currentTemplate.Tasks.Count)
                return;

            var item = _currentTemplate.Tasks[idx];
            _currentTemplate.Tasks.RemoveAt(idx);
            _currentTemplate.Tasks.Insert(target, item);
            RefreshTaskListFromCurrent();
            tasksListBox.SelectedIndex = target;
            _dirty = true;
        }

        private void OnDeleteTaskClick(object sender, RoutedEventArgs e)
        {
            if (_currentTemplate == null)
                return;
            if (!(tasksListBox.SelectedItem is TaskEdit row))
                return;

            _currentTemplate.Tasks.RemoveAll(t => t.Id == row.Id);
            RefreshTaskListFromCurrent();
            _dirty = true;
        }

        // ----- Save / Cancel / Close -----

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            CommitToManager();
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_dirty)
            {
                var r = MessageBox.Show(
                    this,
                    "Discard unsaved changes to templates?",
                    "Unsaved changes",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (r != MessageBoxResult.OK)
                    return;
            }

            DialogResult = false;
            Close();
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (DialogResult.HasValue)
                return;

            if (_dirty)
            {
                var r = MessageBox.Show(
                    this,
                    "Discard unsaved changes to templates?",
                    "Unsaved changes",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (r != MessageBoxResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
            }
            DialogResult = false;
        }

        private void CommitToManager()
        {
            var domain = _templates.Select(te => new TodoTemplate
            {
                Id = te.Id,
                Name = te.Name ?? string.Empty,
                Tasks = te.Tasks.Select(t => new TodoTemplateTask
                {
                    Id = t.Id,
                    Text = t.Text ?? string.Empty
                }).ToList()
            }).ToList();

            _viewItemManager.SetTemplates(domain);

            // If the active template was deleted, clear the active state.
            var activeId = _viewItemManager.ActiveTemplateId;
            if (!string.IsNullOrEmpty(activeId) && !domain.Any(t => t.Id == activeId))
            {
                _viewItemManager.ActiveTemplateId = string.Empty;
                _viewItemManager.SetTemplateProgress(new Dictionary<string, bool>());
            }

            // If the default template was deleted, clear it too.
            var defaultId = _viewItemManager.DefaultTemplateId;
            if (!string.IsNullOrEmpty(defaultId) && !domain.Any(t => t.Id == defaultId))
            {
                _viewItemManager.DefaultTemplateId = string.Empty;
            }

            _viewItemManager.Save();
        }

        // ----- Internal edit models -----

        private class TemplateEdit
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public List<TaskEdit> Tasks { get; set; }
            public override string ToString() => Name;
        }

        private class TaskEdit
        {
            public string Id { get; set; }
            public string Text { get; set; }
            public string DisplayNumber { get; set; }
        }
    }
}
