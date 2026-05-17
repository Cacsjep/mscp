using Avalonia.Controls;
using Mscp.PkiCertInstaller.Services;
using Mscp.PkiCertInstaller.ViewModels;

namespace Mscp.PkiCertInstaller.Views;

public partial class CertDetailsDialog : Window
{
    public CertDetailsDialog()
    {
        InitializeComponent();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    public static System.Threading.Tasks.Task ShowFor(Window owner, CertItemViewModel vm)
    {
        var dlg = new CertDetailsDialog { DataContext = vm };
        return dlg.ShowDialog(owner);
    }

    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
