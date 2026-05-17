using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mscp.PkiCertInstaller.Services;
using Mscp.PkiCertInstaller.ViewModels;

namespace Mscp.PkiCertInstaller.Views;

public partial class AccountPickerDialog : Window
{
    public AccountResolver.AccountEntry? Result { get; private set; }

    public AccountPickerDialog()
    {
        InitializeComponent();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountPickerViewModel vm) Result = vm.Selected;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void OnDoubleTap(object? sender, TappedEventArgs e)
    {
        if (DataContext is AccountPickerViewModel vm && vm.Selected is not null)
        {
            Result = vm.Selected;
            Close();
        }
    }
}
