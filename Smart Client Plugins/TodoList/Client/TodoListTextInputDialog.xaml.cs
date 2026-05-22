using System.Windows;

namespace TodoList.Client
{
    public partial class TodoListTextInputDialog : Window
    {
        public string ResultText { get; private set; }

        public TodoListTextInputDialog(string title, string prompt, string initialValue)
        {
            InitializeComponent();
            Title = title ?? "Input";
            promptText.Text = prompt ?? "Enter value:";
            inputBox.Text = initialValue ?? string.Empty;
            Loaded += (s, e) =>
            {
                inputBox.Focus();
                inputBox.SelectAll();
            };
        }

        public static string Prompt(Window owner, string title, string prompt, string initialValue)
        {
            var dlg = new TodoListTextInputDialog(title, prompt, initialValue);
            if (owner != null)
                dlg.Owner = owner;
            return dlg.ShowDialog() == true ? dlg.ResultText : null;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            ResultText = inputBox.Text?.Trim() ?? string.Empty;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
