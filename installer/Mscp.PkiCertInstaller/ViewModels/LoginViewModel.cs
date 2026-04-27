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

    // The login flow asks the View to show a TLS-trust prompt when the
    // Mgmt Server's cert isn't trusted. The View resolves the bool
    // ("did the admin click Trust?") and the VM either retries the
    // login or reports cancellation back to the user.
    public event EventHandler<TrustPromptArgs>? TrustPromptRequested;
    public sealed class TrustPromptArgs : EventArgs
    {
        public UntrustedServerCertException Exception { get; }
        public TaskCompletionSource<bool> Result { get; } = new();
        public TrustPromptArgs(UntrustedServerCertException ex) { Exception = ex; }
    }

    private static readonly TrustStore _trustStore = new();

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
        try
        {
            Log.Info($"Login attempt: server='{ServerUrl}', mode={Mode}, user='{(NeedsExplicitCreds ? Username : CurrentWindowsIdentity)}'");
            // The first attempt may throw UntrustedServerCertException
            // from inside the TLS validator. On accept the admin pins
            // the thumbprint and we retry exactly once.
            if (await TryLoginAsync()) return;

            // Retried login (after pinning) failed - already reported
            // via ErrorMessage in TryLoginAsync; nothing else to do.
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> TryLoginAsync()
    {
        MilestoneClient? client = null;
        try
        {
            client = new MilestoneClient(ServerUrl, _trustStore);
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
            return true;
        }
        catch (Exception ex)
        {
            var trustEx = MilestoneClient.FindTrustException(ex);
            if (trustEx != null && TrustPromptRequested != null)
            {
                client?.Dispose();
                client = null;
                Log.Warn($"Untrusted TLS cert from {trustEx.Host}:{trustEx.Port} ({trustEx.Reason}); thumbprint {trustEx.Thumbprint}");
                var args = new TrustPromptArgs(trustEx);
                TrustPromptRequested.Invoke(this, args);
                var accepted = await args.Result.Task.ConfigureAwait(true);
                if (!accepted)
                {
                    ErrorMessage = "Server certificate was not trusted.";
                    Log.Info("Admin declined to trust the server certificate.");
                    return false;
                }
                _trustStore.Trust(trustEx.Host, trustEx.Port, trustEx.Thumbprint);
                Log.Info($"Admin pinned thumbprint {trustEx.Thumbprint} for {trustEx.Host}:{trustEx.Port}; retrying.");
                // Recurse exactly once - the pinned thumbprint is now
                // in the store so the validator will accept on retry.
                // If it fails again it falls into the normal error path.
                return await TryLoginAsync();
            }
            Log.Error("Login failed", ex);
            ErrorMessage = Humanize(ex);
            return false;
        }
        finally
        {
            client?.Dispose();
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

        // TLS handshake errors that bypass the typed UntrustedServerCertException
        // path (rare - usually a protocol mismatch or a closed-port reset
        // that LOOKS like TLS). The trust prompt has already had its shot.
        if (Has("SSL") || Has("TLS") || Has("HTTPS handshake"))
            return "TLS handshake failed. Verify the server URL and that the management server is reachable.";

        // Last resort: trim the .NET noise but keep the original line so
        // we never lose information for genuinely unexpected failures.
        var trimmed = (ex.Message ?? "").Trim();
        return trimmed.Length == 0 ? "Login failed (unknown error)." : trimmed;
    }
}
