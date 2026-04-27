using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty] private string serverUrl = "localhost";
    [ObservableProperty] private AuthMode mode = AuthMode.WindowsCurrentUser;
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string currentWindowsIdentity =
        System.Security.Principal.WindowsIdentity.GetCurrent().Name;

    public bool IsBasic            => Mode == AuthMode.Basic;
    public bool IsWindowsCurrent   => Mode == AuthMode.WindowsCurrentUser;
    public bool IsWindowsOther     => Mode == AuthMode.WindowsOtherUser;
    public bool NeedsExplicitCreds => Mode != AuthMode.WindowsCurrentUser;

    partial void OnModeChanged(AuthMode value)
    {
        OnPropertyChanged(nameof(IsBasic));
        OnPropertyChanged(nameof(IsWindowsCurrent));
        OnPropertyChanged(nameof(IsWindowsOther));
        OnPropertyChanged(nameof(NeedsExplicitCreds));
    }

    public event EventHandler<MilestoneClient>? LoginSucceeded;

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            ErrorMessage = "Server URL is required.";
            return;
        }
        if (NeedsExplicitCreds && string.IsNullOrEmpty(Username))
        {
            ErrorMessage = Mode == AuthMode.Basic
                ? "Username is required for Basic auth."
                : "Username is required (use DOMAIN\\user or user@domain).";
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        MilestoneClient? client = null;
        try
        {
            Log.Info($"Login attempt: server='{ServerUrl}', mode={Mode}, user='{(NeedsExplicitCreds ? Username : CurrentWindowsIdentity)}'");
            client = new MilestoneClient(ServerUrl);
            switch (Mode)
            {
                case AuthMode.Basic:
                    await client.LoginBasicAsync(Username, Password);
                    break;
                case AuthMode.WindowsCurrentUser:
                    await client.LoginWindowsCurrentUserAsync();
                    break;
                case AuthMode.WindowsOtherUser:
                    await client.LoginWindowsExplicitAsync(Username, Password);
                    break;
            }
            Log.Info("Login succeeded.");
            LoginSucceeded?.Invoke(this, client);
            client = null; // ownership transferred
        }
        catch (Exception ex)
        {
            Log.Error("Login failed", ex);
            ErrorMessage = Humanize(ex);
        }
        finally
        {
            client?.Dispose();
            IsBusy = false;
        }
    }

    // Maps the raw exceptions HttpClient / IDP throw into single-line,
    // end-user-friendly messages. We deliberately avoid leaking the
    // .NET exception type or stack-style verbiage - the login screen
    // is for ops people, not developers.
    private static string Humanize(Exception ex)
    {
        // Walk to the innermost message - HttpRequestException usually
        // wraps the actual SocketException with the useful detail.
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        var msg = (ex.Message ?? "") + " " + (inner.Message ?? "");

        bool Has(string needle) => msg.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        if (ex is OperationCanceledException || Has("Timeout") || Has("canceled"))
            return "The management server didn't respond. Check the URL and that the server is running.";
        if (Has("No such host") || Has("name resolution") || Has("getaddrinfo")
            || Has("could not be resolved") || Has("Unknown host"))
            return "Server name could not be resolved. Check spelling, DNS, and that you can ping the host.";
        if (Has("actively refused") || Has("connection refused"))
            return "The server is reachable but not accepting connections on that port. Check the URL.";
        if (Has("connection forcibly closed") || Has("connection reset"))
            return "The connection was dropped by the server. The management server may be restarting.";
        if (Has("Unable to connect") || Has("Network is unreachable"))
            return "Couldn't reach the server. Check the URL, network, and firewall.";

        // IDP-specific failures bubble up as "IDP 4xx: { ... }" from
        // MilestoneClient.ExchangeTokenAsync. Translate the common ones.
        if (Has("invalid_grant") || (Has("IDP 400") && Has("invalid_grant")))
            return "Username or password is incorrect.";
        if (Has("IDP 401"))
            return "The management server rejected the credentials. Wrong username, password, or domain.";
        if (Has("IDP 403") || Has("VMO61008"))
            return "The user has no permissions on this server. Add the account to a Role in "
                 + "Management Client - Security - Roles, then try again.";
        if (Has("invalid_client"))
            return "The OAuth client was rejected. Make sure you're connecting to a Milestone XProtect "
                 + "2021 R1 or newer management server.";

        // SSL trust failures - we already trust everything, so this is rare.
        if (Has("SSL") || Has("TLS") || Has("certificate") || Has("HTTPS handshake"))
            return "TLS / certificate handshake failed. Try the http:// scheme if your management server "
                 + "isn't running encrypted, or verify the server certificate.";

        // Last resort: trim the .NET noise but keep the original line so
        // we never lose information for genuinely unexpected failures.
        var trimmed = (ex.Message ?? "").Trim();
        return trimmed.Length == 0 ? "Login failed (unknown error)." : trimmed;
    }
}
