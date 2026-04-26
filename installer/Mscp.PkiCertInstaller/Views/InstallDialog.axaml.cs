using Avalonia.Controls;
using Avalonia.Interactivity;
using Mscp.PkiCertInstaller.Services;
using Mscp.PkiCertInstaller.ViewModels;

namespace Mscp.PkiCertInstaller.Views;

public partial class InstallDialog : Window
{
    public bool Confirmed { get; private set; }

    public InstallDialog()
    {
        InitializeComponent();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    private async void OnAddAccount(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not InstallDialogViewModel vm) return;
        var picker = new AccountPickerDialog { DataContext = new AccountPickerViewModel() };
        await picker.ShowDialog(this);
        if (picker.Result is { } pick)
            vm.TryAddAccount(pick.Name);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
