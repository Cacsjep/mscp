using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TodoList.Client.Models;

namespace TodoList.Client
{
    public partial class TodoListTemplatePickerWindow : Window
    {
        public string SelectedTemplateId { get; private set; }

        public TodoListTemplatePickerWindow(List<TodoTemplate> templates, string preselectId)
        {
            InitializeComponent();

            var rows = templates.Select(t => new TemplateRow
            {
                Id = t.Id,
                Name = string.IsNullOrEmpty(t.Name) ? "(unnamed)" : t.Name,
                TaskSummary = t.Tasks.Count == 1
                    ? "1 task"
                    : $"{t.Tasks.Count} tasks"
            }).ToList();

            templateList.ItemsSource = rows;

            if (!string.IsNullOrEmpty(preselectId))
            {
                var match = rows.FirstOrDefault(r => r.Id == preselectId);
                if (match != null)
                {
                    templateList.SelectedItem = match;
                    templateList.ScrollIntoView(match);
                }
            }
            if (templateList.SelectedItem == null && rows.Count > 0)
                templateList.SelectedIndex = 0;
        }

        private void OnStartClick(object sender, RoutedEventArgs e) => Accept();

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (templateList.SelectedItem != null)
                Accept();
        }

        private void Accept()
        {
            if (templateList.SelectedItem is TemplateRow row)
            {
                SelectedTemplateId = row.Id;
                DialogResult = true;
                Close();
            }
        }

        private class TemplateRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string TaskSummary { get; set; }
        }
    }
}
