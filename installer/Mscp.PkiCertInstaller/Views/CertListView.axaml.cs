using System.Linq;
using Avalonia.Controls;
using Mscp.PkiCertInstaller.ViewModels;

namespace Mscp.PkiCertInstaller.Views;

public partial class CertListView : UserControl
{
    public CertListView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Hook();
    }

    private void Hook()
    {
        if (DataContext is CertListViewModel vm)
        {
            vm.InstallRequested -= OnInstallRequested;
            vm.InstallRequested += OnInstallRequested;
            vm.ResultReady -= OnResultReady;
            vm.ResultReady += OnResultReady;
        }
    }

    private async void OnResultReady(object? sender, CertListViewModel.OpResult r)
    {
        if (VisualRoot is not Window owner) return;
        await ResultDialog.ShowResult(owner, r);
    }

    private async void OnInstallRequested(object? sender, CertListViewModel.InstallRequest req)
    {
        if (VisualRoot is not Window owner) return;

        var dialogVm = new InstallDialogViewModel(req.Items, req.DefaultAccountsCsv);
        var dlg = new InstallDialog { DataContext = dialogVm };
        await dlg.ShowDialog(owner);

        if (!dlg.Confirmed) return;
        if (DataContext is CertListViewModel vm)
            vm.PerformInstall(req.Items, dialogVm.SnapshotValidAccounts());
    }
}
