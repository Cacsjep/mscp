using System;

namespace Mscp.PkiCertInstaller.Models;

// One row in the cert list. Maps directly from the Milestone REST item
// shape (FQID + Properties bag), with two additional locally-computed
// fields: the install state on this machine and a parsed NotAfter.
public sealed class CertItem
{
    public Guid Id { get; init; }
    public Guid Kind { get; init; }
    public string Name { get; init; } = "";
    public string KindLabel { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Thumbprint { get; init; } = "";
    public string IssuerThumbprint { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public DateTime? NotBefore { get; init; }
    public DateTime? NotAfter { get; init; }
    public string KeyAlgorithm { get; init; } = "";
    public bool HasPrivateKey { get; init; }
    public string PfxBase64 { get; init; } = "";
    public string DerBase64 { get; init; } = "";
    public string SubjectAlternativeNames { get; init; } = "";

    public string ShortThumbprint => string.IsNullOrEmpty(Thumbprint)
        ? ""
        : Thumbprint.Length > 16 ? Thumbprint[..8] + "..." + Thumbprint[^8..] : Thumbprint;
}
