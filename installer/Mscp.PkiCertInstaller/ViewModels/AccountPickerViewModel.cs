using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.ViewModels;

public partial class AccountPickerViewModel : ObservableObject
{
    private readonly List<AccountResolver.AccountEntry> _all;

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private AccountResolver.AccountEntry? selected;

    public ObservableCollection<AccountResolver.AccountEntry> Items { get; } = new();

    public AccountPickerViewModel()
    {
        _all = AccountResolver.Enumerate().ToList();
        Refilter();
    }

    partial void OnSearchTextChanged(string value) => Refilter();

    private void Refilter()
    {
        var q = (SearchText ?? "").Trim();
        Items.Clear();
        foreach (var a in _all)
        {
            if (q.Length == 0
                || a.Name.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                || a.Kind.Contains(q, System.StringComparison.OrdinalIgnoreCase))
                Items.Add(a);
        }
    }
}
