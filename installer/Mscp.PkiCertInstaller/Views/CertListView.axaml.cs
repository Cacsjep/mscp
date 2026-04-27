using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mscp.PkiCertInstaller.ViewModels;

namespace Mscp.PkiCertInstaller.Views;

public partial class CertListView : UserControl
{
    private Window? _attachedWindow;

    public CertListView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Hook();

        // The CertListView is created inside MainWindow.ContentControl
        // when CurrentView flips to CertListViewModel. The Window then
        // resizes from the login card (460 wide) to maximized; the
        // DataGrid's first measure pass happens at 460 wide and its
        // * columns settle at MinWidth. When the window subsequently
        // maximizes Avalonia 11.3's DataGrid does NOT reflow its star
        // columns - the column collection caches its measured widths
        // and a same-value re-assignment is treated as a no-op.
        //
        // The fix that actually works: temporarily switch the star
        // columns to Pixel widths, run a layout pass, then switch them
        // back to Star. The type change forces Avalonia to invalidate
        // the cached measurement and recompute against the current
        // available width.
        AttachedToVisualTree += (_, _) => AttachWindowHandlers();
        DetachedFromVisualTree += (_, _) => DetachWindowHandlers();
        SizeChanged += (_, _) => ForceStarColumns();
        LayoutUpdated += OnLayoutUpdated;
    }

    private double _lastWidth = -1;
    private int _layoutKicks;

    private void OnLayoutUpdated(object? sender, System.EventArgs e)
    {
        // Capture the final post-maximize layout pass: every time the
        // grid's available width changes, retrigger the Pixel→Star
        // toggle. Capped at a few kicks so we don't loop forever on
        // a stable layout.
        var g = this.FindControl<DataGrid>("Grid");
        if (g == null) return;
        var w = g.Bounds.Width;
        if (w > 0 && System.Math.Abs(w - _lastWidth) > 0.5 && _layoutKicks < 4)
        {
            _lastWidth = w;
            _layoutKicks++;
            ForceStarColumns();
        }
    }

    private void AttachWindowHandlers()
    {
        if (VisualRoot is not Window w) return;
        if (_attachedWindow == w) return;
        DetachWindowHandlers();
        _attachedWindow = w;
        w.PropertyChanged += OnWindowPropertyChanged;
    }

    private void DetachWindowHandlers()
    {
        if (_attachedWindow == null) return;
        _attachedWindow.PropertyChanged -= OnWindowPropertyChanged;
        _attachedWindow = null;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // The window goes Normal→Maximized after the view is already
        // mounted. Catch both the state flip and the resulting client
        // size change so the grid reflows once the new size is real.
        if (e.Property == Window.WindowStateProperty ||
            e.Property == Window.ClientSizeProperty)
        {
            _layoutKicks = 0;
            ForceStarColumns();
        }
    }

    private void ForceStarColumns()
    {
        var g = this.FindControl<DataGrid>("Grid");
        if (g == null || g.Columns.Count == 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (g.Bounds.Width <= 0) return;

                // Snapshot every star column's current weight so we
                // can restore it after the toggle.
                var stars = new List<(DataGridColumn Col, double Weight)>();
                foreach (var col in g.Columns)
                {
                    if (col.Width.IsStar)
                        stars.Add((col, col.Width.Value));
                }
                if (stars.Count == 0) return;

                // Step 1: kick to Pixel. The TYPE change is what
                // forces Avalonia to drop the cached measurement -
                // re-assigning Star with the same value is a no-op.
                foreach (var (col, _) in stars)
                    col.Width = new DataGridLength(1, DataGridLengthUnitType.Pixel);
                g.InvalidateMeasure();
                g.UpdateLayout();

                // Step 2: restore Star. The grid now distributes the
                // current available width across the star columns
                // using their original weights.
                foreach (var (col, weight) in stars)
                    col.Width = new DataGridLength(weight, DataGridLengthUnitType.Star);
                g.InvalidateMeasure();
                g.UpdateLayout();
            }
            catch
            {
                // The grid may still be tearing down on view switch;
                // a failed reflow is harmless because the next layout
                // pass will retry.
            }
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
