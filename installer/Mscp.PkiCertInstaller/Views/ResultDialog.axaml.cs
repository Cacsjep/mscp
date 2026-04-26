using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.Views;

public partial class ResultDialog : Window
{
    public ResultDialog()
    {
        InitializeComponent();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    public static async System.Threading.Tasks.Task ShowSuccess(Window owner, string title, string summary, string? detail = null)
        => await Show(owner, title, summary, detail, success: true);

    public static async System.Threading.Tasks.Task ShowError(Window owner, string title, string summary, string? detail = null)
        => await Show(owner, title, summary, detail, success: false);

    private static async System.Threading.Tasks.Task Show(Window owner, string title, string summary, string? detail, bool success)
    {
        var dlg = new ResultDialog();
        dlg.TitleText.Text = title;
        dlg.SummaryText.Text = summary;
        dlg.Title = title;

        var iconRes = success ? "IconCheck" : "IconError";
        if (Avalonia.Application.Current!.TryFindResource(iconRes, out var geom)
            && geom is StreamGeometry sg)
        {
            dlg.ResultIcon.Data = sg;
        }
        dlg.ResultIcon.Foreground = success
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6E))
            : new SolidColorBrush(Color.FromRgb(0xE0, 0x63, 0x63));

        if (!string.IsNullOrEmpty(detail))
        {
            dlg.DetailText.Text = detail;
            dlg.DetailBorder.IsVisible = true;
        }

        await dlg.ShowDialog(owner);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
