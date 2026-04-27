using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mscp.PkiCertInstaller.Models;
using Mscp.PkiCertInstaller.Services;

namespace Mscp.PkiCertInstaller.ViewModels;

public partial class CertItemViewModel : ObservableObject
{
    public CertItem Source { get; }

    [ObservableProperty] private InstallState state;

    // Per-row checkbox in the leftmost grid column. Drives the VM's
    // SelectedItems list - simpler than Ctrl/Shift-click, especially
    // for non-power-users.
    [ObservableProperty] private bool isChecked;

    // Set by the parent VM at construction so we can notify it when
    // the user toggles the row checkbox.
    public System.Action<CertItemViewModel>? CheckedNotifier { get; set; }

    partial void OnIsCheckedChanged(bool value) => CheckedNotifier?.Invoke(this);

    public CertItemViewModel(CertItem source)
    {
        Source = source;
        Refresh();
    }

    public string Name => Source.Name;
    public string KindLabel => Source.KindLabel;
    public string Subject => Source.Subject;
    public string Issuer => Source.Issuer;
    public string Thumbprint => Source.Thumbprint;
    public string ShortThumbprint => Source.ShortThumbprint;
    public string KeyAlgorithm => Source.KeyAlgorithm;
    public string ValidUntil => Source.NotAfter?.ToString("yyyy-MM-dd") ?? "";
    public string Remaining
    {
        get
        {
            if (Source.NotAfter is null) return "";
            var d = (int)Math.Round((Source.NotAfter.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays);
            if (d < 0) return $"expired {-d}d ago";
            if (d == 0) return "expires today";
            if (d < 365) return $"{d}d";
            var y = d / 365;
            var rem = d % 365;
            return rem == 0 ? $"{y}y" : $"{y}y {rem}d";
        }
    }
    public bool HasPrivateKey => Source.HasPrivateKey && !string.IsNullOrEmpty(Source.PfxBase64);
    public string Sans => Source.SubjectAlternativeNames;

    // Pipe-separated SAN entries split into a list for the details
    // dialog. Empty entries are dropped; "DNS:"/"IP:" prefixes are
    // preserved so the admin can see the entry type at a glance.
    public System.Collections.Generic.IReadOnlyList<string> SansList
    {
        get
        {
            var raw = Source.SubjectAlternativeNames ?? "";
            if (raw.Length == 0) return System.Array.Empty<string>();
            return raw.Split('|', StringSplitOptions.RemoveEmptyEntries
                                | StringSplitOptions.TrimEntries);
        }
    }

    // Where this cert lands when installed. Used by the install dialog
    // and the Refresh() state probe so the "Installed" column reflects
    // the actual store the cert belongs in (Root / CA / My).
    public System.Security.Cryptography.X509Certificates.StoreName TargetStore
        => CertInstaller.StoreForKind(KindLabel);
    public string TargetStoreDisplay => CertInstaller.StoreDisplayName(TargetStore);
    public bool NeedsKeyAcl => CertInstaller.NeedsKeyAcl(KindLabel);

    // Compact SAN string for the grid column. Strips the "DNS:" / "IP:"
    // prefixes for readability and caps the displayed entries; the full
    // list still rides the ToolTip via the Sans property.
    public string SansShort
    {
        get
        {
            var raw = Sans ?? "";
            if (raw.Length == 0) return "";
            var entries = raw.Split('|', StringSplitOptions.RemoveEmptyEntries
                                       | StringSplitOptions.TrimEntries);
            if (entries.Length == 0) return "";
            string Strip(string e)
                => e.StartsWith("DNS:", StringComparison.OrdinalIgnoreCase) ? e[4..]
                 : e.StartsWith("IP:",  StringComparison.OrdinalIgnoreCase) ? e[3..]
                 : e;
            if (entries.Length <= 2)
                return string.Join(", ", System.Linq.Enumerable.Select(entries, Strip));
            return $"{Strip(entries[0])}, {Strip(entries[1])} (+{entries.Length - 2} more)";
        }
    }

    // ── Machine match ────────────────────────────────────────────────
    // Walks the cert's SAN list and reports whether any DNS or IP entry
    // resolves to this Windows host. Useful at a glance: "is this cert
    // meant for this box, or am I about to install something for a
    // different machine?".

    public bool MatchesMachine => MatchedSan != null;

    // The first SAN entry that matched - shown in the tooltip and
    // detail panel so the admin sees *why* it matched.
    public string? MatchedSan => ComputeMatchedSan();

    public string MachineMatchText => MatchesMachine ? "Yes" : "No";

    public IBrush MachineMatchBrush => MatchesMachine
        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6E))   // green
        : new SolidColorBrush(Color.FromRgb(0x6F, 0x77, 0x82));  // grey

    public string MachineMatchTooltip
    {
        get
        {
            if (MatchesMachine)
                return $"This cert matches this machine via SAN entry: {MatchedSan}\n"
                     + $"Hostname: {Services.MachineIdentity.Hostname}\n"
                     + $"FQDN:     {Services.MachineIdentity.Fqdn}";
            return "No SAN entry on this cert matches this machine's hostname, FQDN, or IP addresses.\n"
                 + $"Hostname: {Services.MachineIdentity.Hostname}\n"
                 + $"FQDN:     {Services.MachineIdentity.Fqdn}";
        }
    }

    private string? ComputeMatchedSan()
    {
        var raw = Source.SubjectAlternativeNames ?? "";
        if (raw.Length == 0) return null;
        foreach (var entry in raw.Split('|'))
        {
            var t = entry.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith("DNS:", StringComparison.OrdinalIgnoreCase))
            {
                if (Services.MachineIdentity.DnsMatches(t[4..])) return t;
            }
            else if (t.StartsWith("IP:", StringComparison.OrdinalIgnoreCase))
            {
                if (Services.MachineIdentity.IpMatches(t[3..])) return t;
            }
        }
        return null;
    }

    // Compact "Yes/No/Expired" label for the status column header
    // ("Installed"). Pairs with StatusBrush below for the dot.
    public string StatusText => State switch
    {
        InstallState.Installed       => "Yes",
        InstallState.InstalledNoKey  => "Yes (no key)",
        InstallState.Expired         => "Expired",
        InstallState.NotInstalled    => "No",
        _ => "—",
    };

    // Green = installed and current. Orange = not installed yet. Red =
    // expired (don't install). Grey = unknown / no data yet.
    public IBrush StatusBrush => State switch
    {
        InstallState.Installed      => new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6E)), // green
        InstallState.InstalledNoKey => new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0x6E)),
        InstallState.Expired        => new SolidColorBrush(Color.FromRgb(0xE0, 0x63, 0x63)), // red
        InstallState.NotInstalled   => new SolidColorBrush(Color.FromRgb(0xF2, 0xA1, 0x3D)), // orange
        _ => new SolidColorBrush(Color.FromRgb(0x6F, 0x77, 0x82)),
    };

    public void Refresh()
    {
        if (Source.NotAfter is { } na && na < DateTime.UtcNow)
        {
            State = InstallState.Expired;
        }
        else if (string.IsNullOrEmpty(Source.Thumbprint))
        {
            State = InstallState.NotInstalled;
        }
        else
        {
            // "Yes (no key)" reflects the LOCAL cert store - someone
            // imported the .cer alone or a prior install lost the
            // private key file. We probe the *target* store for this
            // kind: Root CAs sit in the trust store and are key-less by
            // design, so for those "no key" is normal and we report
            // them as Installed.
            var (installed, localHasKey) = CertInstaller.GetState(Source.Thumbprint, TargetStore);
            if (!installed)            State = InstallState.NotInstalled;
            else if (!NeedsKeyAcl)     State = InstallState.Installed;        // CA cert - no key expected
            else if (localHasKey)      State = InstallState.Installed;
            else                       State = InstallState.InstalledNoKey;
        }
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }
}
