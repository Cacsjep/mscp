using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

    // Selection is driven by the per-row checkbox column; the DataGrid's
    // own row/cell selection is purely cosmetic noise here. We clear it
    // every time the framework tries to apply one. SelectedItems is also
    // cleared so :selected can never re-appear via keyboard navigation.
    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid g) return;
        if (g.SelectedItem is null && g.SelectedItems.Count == 0) return;
        g.SelectedItem = null;
        g.SelectedItems.Clear();
    }
}
