using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.Views;

public partial class HelpDialog : Window
{
    public HelpDialog()
    {
        InitializeComponent();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Log.LogDirectory,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Folder may not exist yet if no logs were written; ignore.
        }
    }
}
