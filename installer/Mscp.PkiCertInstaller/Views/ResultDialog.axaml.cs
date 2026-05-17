using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Mscp.PkiCertInstaller.Services;
using Mscp.PkiCertInstaller.ViewModels;

namespace Mscp.PkiCertInstaller.Views;

public partial class ResultDialog : Window
{
    public ResultDialog()
    {
        InitializeComponent();
        WindowChromeHelper.HookDarkTitleBar(this);
    }

    public static System.Threading.Tasks.Task ShowResult(Window owner, CertListViewModel.OpResult r)
        => Show(owner, r);

    private static async System.Threading.Tasks.Task Show(Window owner, CertListViewModel.OpResult r)
    {
        var dlg = new ResultDialog();
        dlg.TitleText.Text = r.Title;
        dlg.SummaryText.Text = r.Summary;
        dlg.Title = r.Title;

        var iconRes = r.Success ? "IconCheck" : "IconError";
        if (Avalonia.Application.Current!.TryFindResource(iconRes, out var geom)
            && geom is StreamGeometry sg)
        {
            dlg.ResultIcon.Data = sg;
        }
        dlg.ResultIcon.Foreground = r.Success
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6E))
            : new SolidColorBrush(Color.FromRgb(0xE0, 0x63, 0x63));

        if (r.Entries.Count > 0)
        {
            dlg.EntriesList.ItemsSource = r.Entries;
            dlg.EntriesBorder.IsVisible = true;
        }

        await dlg.ShowDialog(owner);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
