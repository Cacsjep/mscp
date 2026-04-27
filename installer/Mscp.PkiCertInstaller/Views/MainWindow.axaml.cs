using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Mscp.PkiCertInstaller.Services;
using Mscp.PkiCertInstaller.ViewModels;

namespace Mscp.PkiCertInstaller.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Hook();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    private void Hook()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged -= OnVmChanged;
            vm.PropertyChanged += OnVmChanged;
            // Initial state: apply size first, then assign content so
            // the first measure of the inner view sees the correct
            // window dimensions.
            ApplyAndSwap(vm);
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.CurrentView)) return;
        if (sender is MainWindowViewModel vm) ApplyAndSwap(vm);
    }

    // Two-phase swap: resize the window to the target dimensions FIRST
    // and let the layout pass settle, then assign the new content. This
    // is the only reliable way to make the DataGrid in CertListView
    // measure its star columns at the maximized client size on first
    // paint - if the binding fires before the resize, the columns cache
    // at the 460-wide login dimensions and never reflow.
    private void ApplyAndSwap(MainWindowViewModel vm)
    {
        ApplyForCurrentView(vm);

        // Defer the content swap until after the WindowState change
        // has propagated through the OS round-trip (WM_SIZE) and the
        // Avalonia layout pass has updated ClientSize. DispatcherPriority
        // .Background runs after Layout/Render, which is exactly when
        // we want the new view to mount.
        Dispatcher.UIThread.Post(() =>
        {
            if (MainContent != null)
                MainContent.Content = vm.CurrentView;
        }, DispatcherPriority.Background);
    }

    // The login screen is meant to look like a tight card; the cert list
    // needs a wide working area. We resize and recenter when the active
    // view changes so each phase gets the dimensions it deserves.
    private void ApplyForCurrentView(MainWindowViewModel vm)
    {
        switch (vm.CurrentView)
        {
            case LoginViewModel:
                // Auto-fit height to whatever the login card needs so we
                // never have a wasted black band under the Connect button.
                WindowState = WindowState.Normal;
                SizeToContent = SizeToContent.Height;
                Width = 460;
                MinWidth = 420; MinHeight = 0;
                CanResize = false;
                break;
            case CertListViewModel:
                SizeToContent = SizeToContent.Manual;
                CanResize = true;
                MinWidth = 1280; MinHeight = 720;
                Width = 1400; Height = 900;   // restore size if un-maximized
                // The cert list view earns the full screen - lots of
                // columns plus a side detail panel. Maximize on entry
                // so the admin doesn't fight with corner-drag resize.
                WindowState = WindowState.Maximized;
                break;
        }
        // Recenter on the primary screen each switch so the larger grid
        // doesn't end up partly off-screen on small monitors. Skipped
        // when maximized since Position is ignored anyway.
        if (WindowState == WindowState.Maximized) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (Screens?.Primary is { } s)
        {
            var area = s.WorkingArea;
            var widthDip  = Width;
            var heightDip = double.IsNaN(Height) || Height <= 0 ? 540.0 : Height;
            Position = new Avalonia.PixelPoint(
                area.X + (int)((area.Width  - widthDip  * s.Scaling) / 2),
                area.Y + (int)((area.Height - heightDip * s.Scaling) / 2));
        }
    }
}
