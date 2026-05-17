using Avalonia.Controls;
using Avalonia.Interactivity;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.Views;

public partial class HttpFallbackDialog : Window
{
    public bool Confirmed { get; private set; }

    public HttpFallbackDialog()
    {
        InitializeComponent();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    public static async System.Threading.Tasks.Task<bool> AskAsync(Window owner, string proposedHttpUrl)
    {
        var dlg = new HttpFallbackDialog();
        dlg.UrlText.Text = proposedHttpUrl;
        await dlg.ShowDialog(owner);
        return dlg.Confirmed;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
