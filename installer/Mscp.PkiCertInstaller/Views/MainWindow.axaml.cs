using System;
using System.ComponentModel;
using Avalonia.Controls;
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
            ApplyForCurrentView(vm);
        }
    }

    // The login screen is meant to look like a tight card; the cert list
    // needs a wide working area. We resize and recenter when the active
    // view changes so each phase gets the dimensions it deserves.
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.CurrentView)) return;
        if (sender is MainWindowViewModel vm) ApplyForCurrentView(vm);
    }

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
                MinWidth = 1100; MinHeight = 700;
                // The cert list view earns the full screen - lots of
                // columns plus a side detail panel. Maximize on entry
                // so the admin doesn't fight with corner-drag resize.
                WindowState = WindowState.Maximized;
                Width = 1400; Height = 900;   // restore size if un-maximized
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
