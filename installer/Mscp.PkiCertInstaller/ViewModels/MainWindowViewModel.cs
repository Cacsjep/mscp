using CommunityToolkit.Mvvm.ComponentModel;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private object? currentView;

    public MainWindowViewModel()
    {
        var login = new LoginViewModel();
        login.LoginSucceeded += (_, client) =>
        {
            var list = new CertListViewModel(client);
            CurrentView = list;
            list.Reload();
        };
        CurrentView = login;
    }
}
