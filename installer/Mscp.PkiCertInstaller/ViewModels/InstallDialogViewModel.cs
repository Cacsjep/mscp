using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.ViewModels;

public partial class InstallDialogViewModel : ObservableObject
{
    [ObservableProperty] private AccountRow? selectedAccount;

    public string Heading { get; }
    public ObservableCollection<string> Items { get; }
    public ObservableCollection<AccountRow> Accounts { get; } = new();

    public bool AnyNeedsKeyAcl { get; }

    public InstallDialogViewModel(IReadOnlyList<CertItemViewModel> items, string defaultAccountsCsv)
    {
        Items = new ObservableCollection<string>(items.Select(i =>
            $"{i.KindLabel}  -  {i.Name}\n   -> {i.TargetStoreDisplay}"));
        Heading = items.Count == 1
            ? "Install 1 certificate"
            : $"Install {items.Count} certificates";
        AnyNeedsKeyAcl = items.Any(i => i.NeedsKeyAcl);
        foreach (var a in (defaultAccountsCsv ?? "")
                     .Split(',', System.StringSplitOptions.RemoveEmptyEntries
                              | System.StringSplitOptions.TrimEntries))
            Accounts.Add(AccountRow.From(a));
    }

    [RelayCommand]
    private void RemoveAccount()
    {
        if (SelectedAccount is null) return;
        Accounts.Remove(SelectedAccount);
        SelectedAccount = null;
    }

    public bool TryAddAccount(string name)
    {
        var t = (name ?? "").Trim();
        if (t.Length == 0) return false;
        if (Accounts.Any(a => string.Equals(a.Name, t, System.StringComparison.OrdinalIgnoreCase)))
            return false;
        Accounts.Add(AccountRow.From(t));
        return true;
    }

    // Final list of account names to grant Read on the key file. Skips
    // any that didn't resolve to a real SID so we don't blow up the
    // ACL apply call.
    public IReadOnlyList<string> SnapshotValidAccounts()
        => Accounts.Where(a => a.IsValid).Select(a => a.Name).ToList();

    // Per-row state shown in the dialog list. Validity is computed up
    // front so the user sees a green check / red X next to each entry.
    public sealed class AccountRow
    {
        public string Name { get; }
        public bool IsValid { get; }
        public string StatusText => IsValid ? "✓" : "✗";
        public IBrush StatusBrush => IsValid
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6E))
            : new SolidColorBrush(Color.FromRgb(0xE0, 0x63, 0x63));
        public string Tooltip => IsValid
            ? "Account resolves to a valid Windows SID."
            : "Account could not be resolved on this machine. It will be skipped during install.";

        private AccountRow(string name, bool valid) { Name = name; IsValid = valid; }
        public static AccountRow From(string name) => new(name, AccountResolver.Exists(name));
    }
}
