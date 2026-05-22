using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using TodoList.Client.Models;
using VideoOS.Platform.Client;

namespace TodoList.Client
{
    public partial class TodoListPropertiesWpfUserControl : PropertiesWpfUserControl
    {
        private readonly TodoListViewItemManager _viewItemManager;
        private bool _loading;

        public TodoListPropertiesWpfUserControl(TodoListViewItemManager viewItemManager)
        {
            _viewItemManager = viewItemManager;
            InitializeComponent();
        }

        public override void Init()
        {
            _loading = true;
            try
            {
                titleTextBox.Text = _viewItemManager.Title;
                fontSizeTextBox.Text = _viewItemManager.FontSize;

                if (_viewItemManager.Mode == TodoListViewItemManager.ModeTemplate)
                    templateModeRadio.IsChecked = true;
                else
                    freeModeRadio.IsChecked = true;

                RefreshTemplateCombo();
            }
            finally
            {
                _loading = false;
            }
        }

        public override void Close()
        {
            _viewItemManager.Title = (titleTextBox.Text ?? string.Empty).Trim();

            var fontSizeText = (fontSizeTextBox.Text ?? string.Empty).Trim();
            if (double.TryParse(fontSizeText, out var size) && size > 0 && size <= 72)
                _viewItemManager.FontSize = fontSizeText;

            _viewItemManager.Mode = templateModeRadio.IsChecked == true
                ? TodoListViewItemManager.ModeTemplate
                : TodoListViewItemManager.ModeFree;

            _viewItemManager.DefaultTemplateId =
                defaultTemplateCombo.SelectedValue as string ?? string.Empty;

            _viewItemManager.Save();
            _viewItemManager.RaiseConfigurationChanged();
        }

        private void RefreshTemplateCombo()
        {
            var templates = _viewItemManager.GetTemplates();

            templateCountText.Text = templates.Count == 1
                ? "1 defined"
                : $"{templates.Count} defined";

            var rows = new List<TemplateRow>
            {
                new TemplateRow { Id = string.Empty, Name = "(none)" }
            };
            rows.AddRange(templates.Select(t => new TemplateRow
            {
                Id = t.Id,
                Name = string.IsNullOrEmpty(t.Name) ? "(unnamed)" : t.Name
            }));

            defaultTemplateCombo.ItemsSource = rows;

            var current = _viewItemManager.DefaultTemplateId ?? string.Empty;
            var match = rows.FirstOrDefault(r => r.Id == current) ?? rows[0];
            defaultTemplateCombo.SelectedValue = match.Id;

            defaultTemplateCombo.IsEnabled =
                templateModeRadio.IsChecked == true && templates.Count > 0;
        }

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;
            defaultTemplateCombo.IsEnabled =
                templateModeRadio.IsChecked == true &&
                (defaultTemplateCombo.ItemsSource as IEnumerable<TemplateRow>)?.Count() > 1;
        }

        private void OnManageTemplatesClick(object sender, RoutedEventArgs e)
        {
            var win = new TodoListTemplatesWindow(_viewItemManager);
            try
            {
                var owner = Window.GetWindow(this);
                if (owner != null)
                    win.Owner = owner;
                else
                    new WindowInteropHelper(win).Owner = NativeOwner();
            }
            catch { }

            var result = win.ShowDialog();
            if (result == true)
            {
                // TodoListTemplatesWindow.CommitToManager() already saved.
                _viewItemManager.RaiseConfigurationChanged();
                RefreshTemplateCombo();
            }
        }

        private static IntPtr NativeOwner()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
            catch { return IntPtr.Zero; }
        }

        private class TemplateRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
}
