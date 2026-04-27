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

        // The CertListView is created inside MainWindow.ContentControl
        // when CurrentView flips to CertListViewModel. The Window then
        // resizes from the login card (460 wide) to maximized; if the
        // DataGrid's first measure pass happens BEFORE that resize, its
        // * columns settle at the column MinWidths and never re-flow.
        // Avalonia 11.3's DataGrid caches the * column widths from the
        // first measure pass and InvalidateMeasure alone doesn't make
        // it recompute. The reliable fix is to re-assign Width on the
        // * columns themselves: assigning a DataGridLength forces the
        // column to re-measure with the current available width.
        SizeChanged += (_, _) => ForceStarColumns();
    }

    private void ForceStarColumns()
    {
        var g = this.FindControl<DataGrid>("Grid");
        if (g == null || g.Bounds.Width <= 0 || g.Columns.Count == 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            // Re-assigning the same DataGridLength.Star value triggers
            // a fresh column measurement with the actual available
            // width. Wrapped in try/catch because the grid may still
            // be tearing down on view switch.
            try
            {
                foreach (var col in g.Columns)
                {
                    if (col.Width.IsStar)
                        col.Width = new DataGridLength(col.Width.Value, DataGridLengthUnitType.Star);
                }
                g.InvalidateMeasure();
                g.UpdateLayout();
            }
            catch { }
        }, DispatcherPriority.Background);
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

    // First Loaded handler: same recipe as SizeChanged, applied once
    // the grid is hooked into the visual tree. Re-asserts * column
    // widths so they reflow against the actual available width.
    private void OnGridLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ForceStarColumns();
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
