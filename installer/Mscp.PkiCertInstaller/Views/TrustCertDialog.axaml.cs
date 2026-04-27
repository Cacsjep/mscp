using Avalonia.Controls;
using Avalonia.Interactivity;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.Views;

public partial class TrustCertDialog : Window
{
    public bool Confirmed { get; private set; }

    public TrustCertDialog()
    {
        InitializeComponent();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    public static async System.Threading.Tasks.Task<bool> AskAsync(
        Window owner, UntrustedServerCertException ex)
    {
        var dlg = new TrustCertDialog();
        dlg.HostText.Text = $"{ex.Host}:{ex.Port}";
        dlg.ReasonText.Text = ex.Reason;
        dlg.SubjectText.Text = string.IsNullOrWhiteSpace(ex.Subject) ? "(none)" : ex.Subject;
        dlg.IssuerText.Text  = string.IsNullOrWhiteSpace(ex.Issuer)  ? "(none)" : ex.Issuer;
        dlg.NotBeforeText.Text = ex.NotBefore == System.DateTime.MinValue
            ? "(unknown)" : ex.NotBefore.ToString("yyyy-MM-dd HH:mm");
        dlg.NotAfterText.Text  = ex.NotAfter == System.DateTime.MinValue
            ? "(unknown)" : ex.NotAfter.ToString("yyyy-MM-dd HH:mm");
        dlg.ThumbprintText.Text = FormatThumbprint(ex.Thumbprint);
        await dlg.ShowDialog(owner);
        return dlg.Confirmed;
    }

    // SHA-1 thumbprint as colon-separated bytes so it lines up with
    // what Windows / browsers / OpenSSL show. Easier for an ops admin
    // to compare against an out-of-band value.
    private static string FormatThumbprint(string raw)
    {
        var t = (raw ?? "").Replace(":", "").Replace(" ", "").ToUpperInvariant();
        if (t.Length == 0) return "(none)";
        var sb = new System.Text.StringBuilder(t.Length + t.Length / 2);
        for (int i = 0; i < t.Length; i++)
        {
            if (i > 0 && i % 2 == 0) sb.Append(':');
            sb.Append(t[i]);
        }
        return sb.ToString();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private void OnTrustClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
