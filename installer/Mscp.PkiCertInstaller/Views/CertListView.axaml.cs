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

    // First Loaded fires before the DataGrid has its final width, so
    // the * columns can settle to their MinWidth instead of consuming
    // the leftover space - the grid only "snaps right" once the user
    // resizes the window. Posting an InvalidateMeasure() at Background
    // priority forces a second layout pass with the actual width and
    // the * columns claim their fair share immediately.
    private void OnGridLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not DataGrid g) return;
        Dispatcher.UIThread.Post(() =>
        {
            g.InvalidateMeasure();
            g.InvalidateArrange();
            g.UpdateLayout();
        }, DispatcherPriority.Background);
    }
}
