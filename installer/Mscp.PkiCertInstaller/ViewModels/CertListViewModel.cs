using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mscp.PkiCertInstaller.Models;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.ViewModels;

public partial class CertListViewModel : ObservableObject, IDisposable
{
    private readonly MilestoneClient _client;
    private readonly List<CertItemViewModel> _all = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string folderFilter = "All folders";

    // Multi-selection list, written by the View's SelectionChanged handler.
    public ObservableCollection<CertItemViewModel> SelectedItems { get; } = new();

    // The list shown in the DataGrid (after search + folder filter).
    public ObservableCollection<CertItemViewModel> Items { get; } = new();

    public string ServerLabel => _client.BaseUri.ToString();

    // Default service identity the cert keys get ACL'd for. Per the
    // Milestone Certificates Guide (p.104, p.127): "By default,
    // XProtect software uses the NETWORK SERVICE account." Anything
    // else (domain service accounts, NT SERVICE\<svc> virtual
    // accounts) is install-specific and the admin adds it via the
    // picker.
    public string DefaultGrantAccountsCsv { get; } = "NETWORK SERVICE";

    // Filter dropdown source. We deliberately omit HTTPS and 802.1X
    // here: the installer's primary use case is deploying Root /
    // Intermediate / Service certs onto Recording / Mgmt / Mobile /
    // Event / Failover boxes. HTTPS-server-only and 802.1X kinds still
    // appear in the grid - the dropdown just doesn't surface them as
    // a quick filter.
    public IReadOnlyList<string> FolderOptions { get; } = new[]
    {
        "All folders", "Root CA", "Intermediate CA", "Service",
    };

    // Raised by InstallCommand so the View can spawn the modal account
    // editor. The View calls back into PerformInstall() with the
    // user's chosen account list once they confirm.
    public event EventHandler<InstallRequest>? InstallRequested;
    // Asks the View to show a result dialog (success/error) instead
    // of stuffing text into a status bar.
    public event EventHandler<OpResult>? ResultReady;

    public sealed record InstallRequest(IReadOnlyList<CertItemViewModel> Items, string DefaultAccountsCsv);
    public sealed record OpResult(bool Success, string Title, string Summary, IReadOnlyList<OpResultEntry> Entries);
    public sealed record OpResultEntry(bool Success, string Name, string Status);

    public CertListViewModel(MilestoneClient client) { _client = client; }

    partial void OnSearchTextChanged(string value)  => Refilter();
    partial void OnFolderFilterChanged(string value) => Refilter();

    [RelayCommand]
    public async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<CertItem> certs;
            try { certs = await _client.ListPkiCertsAsync(); }
            catch (Exception ex)
            {
                ResultReady?.Invoke(this, new OpResult(false,
                    "Reload failed",
                    "Couldn't pull the certificate list from the management server.",
                    new[] { new OpResultEntry(false, "GET /mipKinds/.../mipItems", ex.Message) }));
                return;
            }
            SelectedItems.Clear();
            _all.Clear();
            foreach (var c in certs.Where(c => !string.IsNullOrEmpty(c.Thumbprint))
                                   .OrderBy(c => c.KindLabel)
                                   .ThenBy(c => c.Name))
            {
                var vm = new CertItemViewModel(c) { CheckedNotifier = OnItemChecked };
                _all.Add(vm);
            }
            Refilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Reload() => _ = ReloadAsync();

    private void OnItemChecked(CertItemViewModel vm)
    {
        if (vm.IsChecked)
        {
            if (!SelectedItems.Contains(vm)) SelectedItems.Add(vm);
        }
        else
        {
            SelectedItems.Remove(vm);
        }
        OnPropertyChanged(nameof(AllChecked));
    }

    // Header checkbox state. Three-state so the user sees indeterminate
    // ("some checked"), checked ("all visible checked"), or empty
    // ("none checked"). Setting it from the UI applies to every
    // currently-visible row.
    public bool? AllChecked
    {
        get
        {
            if (Items.Count == 0) return false;
            int checkedCount = Items.Count(i => i.IsChecked);
            if (checkedCount == 0) return false;
            if (checkedCount == Items.Count) return true;
            return null;
        }
        set
        {
            if (value is null) return;     // user can't manually pick indeterminate
            var on = value.Value;
            foreach (var item in Items) item.IsChecked = on;
            OnPropertyChanged(nameof(AllChecked));
        }
    }

    private void Refilter()
    {
        var q = (SearchText ?? "").Trim();
        var folder = FolderFilter ?? "All folders";

        bool MatchesSearch(CertItemViewModel vm)
        {
            if (q.Length == 0) return true;
            return vm.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || vm.Subject.Contains(q, StringComparison.OrdinalIgnoreCase)
                || vm.Issuer.Contains(q, StringComparison.OrdinalIgnoreCase)
                || vm.Thumbprint.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (vm.Sans ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
        }
        bool MatchesFolder(CertItemViewModel vm)
            => folder == "All folders" || vm.KindLabel == folder;

        Items.Clear();
        foreach (var vm in _all)
            if (MatchesSearch(vm) && MatchesFolder(vm)) Items.Add(vm);
        OnPropertyChanged(nameof(AllChecked));
    }

    [RelayCommand]
    private void Install()
    {
        if (SelectedItems.Count == 0)
        {
            ResultReady?.Invoke(this, new OpResult(false,
                "Nothing to install",
                "Tick the certificates you want to install first.",
                Array.Empty<OpResultEntry>()));
            return;
        }
        // Leaf certs need the private key to be useful; trust-store
        // certs (Root / Intermediate) install fine without one.
        var installable = SelectedItems
            .Where(s => !s.NeedsKeyAcl || s.HasPrivateKey).ToList();
        if (installable.Count == 0)
        {
            ResultReady?.Invoke(this, new OpResult(false,
                "No installable rows",
                "None of the ticked leaf certificates have a private key on the server. Re-issue with a key, or pick different rows.",
                Array.Empty<OpResultEntry>()));
            return;
        }
        InstallRequested?.Invoke(this, new InstallRequest(installable, DefaultGrantAccountsCsv));
    }

    // Walks IssuerThumbprint up from the picked cert to its root,
    // returning [leaf, intermediate(s)..., root]. Stops cleanly on
    // self-signed certs, missing parents, or cycles.
    private List<CertItemViewModel> ResolveChain(CertItemViewModel leaf)
    {
        var chain = new List<CertItemViewModel> { leaf };
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { leaf.Thumbprint };
        var current = leaf;
        while (true)
        {
            var issuerThumb = (current.Source.IssuerThumbprint ?? "").Trim();
            if (issuerThumb.Length == 0) break;                                            // root or import
            if (string.Equals(issuerThumb, current.Thumbprint, StringComparison.OrdinalIgnoreCase))
                break;                                                                      // self-signed
            var parent = _all.FirstOrDefault(x =>
                string.Equals(x.Thumbprint, issuerThumb, StringComparison.OrdinalIgnoreCase));
            if (parent is null) break;                                                      // chain truncated
            if (!seen.Add(parent.Thumbprint)) break;                                        // cycle
            chain.Add(parent);
            current = parent;
        }
        return chain;
    }

    // Invoked by the View after the install dialog returns OK. For
    // each ticked cert we walk its chain and install root → CA →
    // ... → leaf so trust is in place before the leaf binds. The
    // result dialog shows one row per ticked cert with a clean
    // success / error status; per-step diagnostics still go to the
    // log if anyone needs them.
    public void PerformInstall(IReadOnlyList<CertItemViewModel> items, IReadOnlyList<string> accounts)
    {
        var entries = new List<OpResultEntry>();
        var ok = 0;
        foreach (var picked in items)
        {
            var chain = ResolveChain(picked);
            string? error = null;
            try
            {
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    var node = chain[i];
                    if (string.IsNullOrEmpty(node.Source.PfxBase64)) continue;
                    var pfx = Convert.FromBase64String(node.Source.PfxBase64);
                    CertInstaller.InstallPfx(
                        pfx, "",
                        targetStore: node.TargetStore,
                        aclPrivateKey: node.NeedsKeyAcl,
                        friendlyName: node.Name,
                        grantAccounts: node.NeedsKeyAcl ? accounts.ToArray() : Array.Empty<string>());
                    node.Refresh();
                }
                var top = chain[chain.Count - 1];
                var topIssuer = (top.Source.IssuerThumbprint ?? "").Trim();
                if (topIssuer.Length > 0 &&
                    !string.Equals(topIssuer, top.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    // Trust path won't validate without the missing CA.
                    error = $"Trust chain incomplete - issuer of \"{top.Name}\" not in vault.";
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error == null)
            {
                ok++;
                entries.Add(new OpResultEntry(true, picked.Name, "Successful"));
            }
            else
            {
                entries.Add(new OpResultEntry(false, picked.Name, error));
            }
        }
        bool success = ok == items.Count;
        var summary = success
            ? $"Installed {ok} certificate(s)."
            : $"{ok} of {items.Count} succeeded.";
        ResultReady?.Invoke(this, new OpResult(success,
            success ? "Install successful" : "Install partially failed",
            summary, entries));
    }

    [RelayCommand]
    private void Uninstall()
    {
        if (SelectedItems.Count == 0)
        {
            ResultReady?.Invoke(this, new OpResult(false,
                "Nothing to uninstall",
                "Tick the certificates you want to uninstall first.",
                Array.Empty<OpResultEntry>()));
            return;
        }
        var entries = new List<OpResultEntry>();
        var ok = 0;
        foreach (var vm in SelectedItems.ToList())
        {
            try
            {
                if (CertInstaller.RemoveByThumbprint(vm.Source.Thumbprint, vm.TargetStore))
                {
                    ok++;
                    entries.Add(new OpResultEntry(true, vm.Name, "Removed"));
                }
                else
                {
                    entries.Add(new OpResultEntry(true, vm.Name, "Not installed (skipped)"));
                }
                vm.Refresh();
            }
            catch (Exception ex)
            {
                entries.Add(new OpResultEntry(false, vm.Name, ex.Message));
            }
        }
        ResultReady?.Invoke(this, new OpResult(true,
            "Uninstall complete",
            $"Removed {ok} certificate(s).", entries));
    }

    [RelayCommand]
    private void OpenServerConfigurator()
    {
        if (!ServerConfiguratorLauncher.TryLaunch(out var err))
            ResultReady?.Invoke(this, new OpResult(false,
                "Could not launch Server Configurator", err, Array.Empty<OpResultEntry>()));
    }

    public void Dispose() => _client.Dispose();
}
