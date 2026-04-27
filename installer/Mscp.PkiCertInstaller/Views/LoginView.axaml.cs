using Avalonia.Controls;
using Avalonia.VisualTree;
using Mscp.PkiCertInstaller.ViewModels;

namespace Mscp.PkiCertInstaller.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Hook();
    }

    private LoginViewModel? _bound;

    private void Hook()
    {
        if (ReferenceEquals(_bound, DataContext)) return;
        if (_bound != null) _bound.TrustPromptRequested -= OnTrustPrompt;
        _bound = DataContext as LoginViewModel;
        if (_bound != null) _bound.TrustPromptRequested += OnTrustPrompt;
    }

    private async void OnTrustPrompt(object? sender, LoginViewModel.TrustPromptArgs e)
    {
        try
        {
            if (this.GetVisualRoot() is not Window owner)
            {
                e.Result.TrySetResult(false);
                return;
            }
            var ok = await TrustCertDialog.AskAsync(owner, e.Exception);
            e.Result.TrySetResult(ok);
        }
        catch (System.Exception ex)
        {
            // Never leave the awaiter dangling - the VM will hang
            // on Result.Task otherwise.
            e.Result.TrySetException(ex);
        }
    }
}
