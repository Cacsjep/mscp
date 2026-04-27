using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            vm.HelpRequested -= OnHelpRequested;
            vm.HelpRequested += OnHelpRequested;
        }
    }

    private async void OnHelpRequested(object? sender, System.EventArgs e)
    {
        if (VisualRoot is not Window owner) return;
        var dlg = new HelpDialog();
        await dlg.ShowDialog(owner);
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

    // Open the certificate details dialog when the admin double-taps a
    // row. We walk up the visual tree from the tap source so a click
    // anywhere on the row (text, icon, even within an inner StackPanel)
    // resolves to the same row's view model. Clicks on the checkbox cell
    // are ignored - the checkbox handles its own toggling and shouldn't
    // also pop a dialog.
    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (VisualRoot is not Window owner) return;
        if (e.Source is not Visual src) return;

        // Don't open the dialog when the tap landed inside a checkbox
        // (the leftmost column is interactive on its own).
        var cb = src.FindAncestorOfType<CheckBox>();
        if (cb != null) return;

        var row = src.FindAncestorOfType<DataGridRow>();
        if (row?.DataContext is CertItemViewModel vm)
            await CertDetailsDialog.ShowFor(owner, vm);
    }

    // The DataGrid still tracks an internal SelectedItem when the user
    // clicks a row, even though we hide every selection visual. Some
    // theme paths (the per-cell focus rectangle in particular) re-derive
    // their visibility from the selection state and pop a "big border"
    // on click. Clearing the selection via Dispatcher.Post avoids the
    // re-entrant crash we saw with an inline mutation - by the time the
    // post runs, the framework's internal selection bookkeeping is done.
    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid g) return;
        if (g.SelectedItem == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                g.SelectedIndex = -1;
            }
            catch
            {
                // Some Avalonia versions throw if SelectionMode disallows -1
                // mid-event; swallow because the visual is already neutered.
            }
        }, DispatcherPriority.Background);
    }
}
