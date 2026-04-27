using System;
using System.Collections.Generic;
using System.Windows.Forms;
using PKI.Crypto;
using PKI.Storage;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace PKI.Admin
{
    // Base ItemManager for every leaf folder (Root, Intermediate, HTTPS, 802.1X,
    // Service). Subclasses set RolePreset; everything else is shared.
    //
    // v1 behavior: cert + key generation runs inside the Management Client
    // process when the admin clicks Save (ValidateAndSaveUserControl). The PFX
    // bundle is stored as base64 on the MIP item ("Pfx" property). After
    // first issue the form goes read-only and shows export buttons.
    public abstract class PkiCertItemManager : ItemManager
    {
        protected readonly Guid Kind;
        protected PkiCertItemManager(Guid kind) { Kind = kind; }
        public abstract RolePreset RolePreset { get; }

        // Subclass picks the per-role help page (RootCA, HTTPS, ...).
        // Returned by GenerateOverviewUserControl below as the user
        // control shown when the admin clicks the FOLDER node in the
        // tree (before drilling into an individual cert).
        protected abstract string HelpFileName { get; }

        // Per-folder security action ID. Each subclass returns its own
        // PKIDefinition.ActionRead* constant; HasReadPermission gates
        // GetItems/GetItem with this action so e.g. the Root CA folder
        // can be locked down independently of HTTPS leaf certs.
        protected abstract string ReadActionId { get; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        public override ItemNodeUserControl GenerateOverviewUserControl()
        {
            return new CommunitySDK.HtmlHelpItemNodeUserControl(
                System.Reflection.Assembly.GetExecutingAssembly(),
                "Admin", HelpFileName);
        }

        // ── User control ─────────────────────────────────────────────────

        private PkiCertificateUserControl _uc;

        public override UserControl GenerateDetailUserControl()
        {
            _uc = new PkiCertificateUserControl(RolePreset, this);
            _uc.ConfigurationChangedByUser += (s, e) => ConfigurationChangedByUserHandler(s, e);
            return _uc;
        }

        public override void ReleaseUserControl()
        {
            if (_uc != null) { _uc = null; }
        }

        public override void FillUserControl(Item item)
        {
            CurrentItem = item;
            _uc?.FillContent(item);
        }

        public override void ClearUserControl()
        {
            CurrentItem = null;
            _uc?.ClearContent();
        }

        public override bool ValidateAndSaveUserControl()
        {
            if (CurrentItem == null || _uc == null) return true;

            // First save → generate the cert. Subsequent saves of an issued
            // cert only persist friendly-name changes (the form is read-only
            // for everything else once issued).
            bool isIssued = !string.IsNullOrEmpty(GetProp(CurrentItem, "Thumbprint"));

            if (!isIssued)
            {
                var validation = _uc.ValidateInput();
                if (validation != null)
                {
                    MessageBox.Show(validation, "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                _uc.UpdateItem(CurrentItem);

                // Reject duplicate display names across the whole plugin
                // tree. The ItemNode framework happily accepts dup names,
                // which then surfaces as confusion in Overview / Issuing
                // CA dropdowns.
                var newName = (CurrentItem.Name ?? "").Trim();
                if (newName.Length > 0 && IsDuplicateName(newName, CurrentItem))
                {
                    MessageBox.Show(
                        $"Another certificate already uses the name \"{newName}\".\n\nPick a unique display name.",
                        "PKI - duplicate name",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                try
                {
                    GenerateAndStore(CurrentItem);
                }
                catch (Exception ex)
                {
                    PKIDefinition.Log.Error($"Cert generation failed: {ex}");
                    MessageBox.Show("Certificate generation failed:\n\n" + ex.Message,
                        "PKI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            else
            {
                _uc.UpdateItem(CurrentItem);
            }

            Configuration.Instance.SaveItemConfiguration(PKIDefinition.PluginId, CurrentItem);

            // Refresh the form so it transitions to the issued/read-only view.
            _uc.FillContent(CurrentItem);
            return true;
        }

        // ── Item Management ──────────────────────────────────────────────

        public override string GetItemName()
        {
            return CurrentItem?.Name ?? "Certificate";
        }

        public override void SetItemName(string name)
        {
            if (CurrentItem != null) CurrentItem.Name = name;
        }

        // PKIDefinition.HasReadPermission(ReadActionId) gates every read
        // entry point. Without it the REST mipItems surface and the Mgmt
        // Client tree would happily hand out base64-encoded PFX bytes
        // (cert + private key) to any AD user who can reach the
        // management server. Each ItemManager checks ITS folder's action
        // (Root CA, Intermediate, HTTPS, 802.1X, Service) so admins can
        // grant narrow, per-folder access.
        public override List<Item> GetItems()
        {
            if (!PKIDefinition.HasReadPermission(ReadActionId)) return new List<Item>();
            return Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, Kind);
        }

        public override List<Item> GetItems(Item parentItem)
        {
            if (!PKIDefinition.HasReadPermission(ReadActionId)) return new List<Item>();
            return Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, parentItem, Kind);
        }

        public override Item GetItem(FQID fqid)
        {
            if (!PKIDefinition.HasReadPermission(ReadActionId)) return null;
            return Configuration.Instance.GetItemConfiguration(PKIDefinition.PluginId, Kind, fqid.ObjectId);
        }

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New " + RolePresets.DisplayName(RolePreset));
            CurrentItem.Properties["RolePreset"]   = RolePreset.ToString();
            CurrentItem.Properties["ValidityDays"] = RolePresets.For(RolePreset).DefaultValidityDays.ToString();
            CurrentItem.Properties["KeyAlgorithmRequest"] = "RSA-2048";
            CurrentItem.Properties["CreatedAt"]    = DateTime.UtcNow.ToString("o");
            Configuration.Instance.SaveItemConfiguration(PKIDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item == null) return;

            // Block delete if any other cert references this one as issuer.
            var thumb = GetProp(item, "Thumbprint");
            if (!string.IsNullOrEmpty(thumb))
            {
                var dependents = FindCertsIssuedBy(thumb);
                if (dependents.Count > 0)
                {
                    var names = string.Join("\n  • ", dependents);
                    MessageBox.Show(
                        "This certificate has issued other certificates and cannot be deleted.\n\n"
                        + "Dependent certificates:\n  • " + names
                        + "\n\nDelete or re-issue those first.",
                        "PKI - delete blocked",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Configuration.Instance.DeleteItemConfiguration(PKIDefinition.PluginId, item);
        }

        // ── Cert generation ──────────────────────────────────────────────

        private void GenerateAndStore(Item item)
        {
            // CN is optional in the form. When empty, fall back to the display
            // name so the cert always has a sensible Subject CN. Browsers don't
            // verify hostnames against CN any more (SAN does that), but CN still
            // shows up in cert dumps and chain inspectors so it shouldn't be
            // empty.
            var cn = GetProp(item, "Subject_CN");
            if (string.IsNullOrWhiteSpace(cn)) cn = item.Name;

            var subject = new CertSubject
            {
                CommonName         = cn,
                Organization       = GetProp(item, "Subject_O"),
                OrganizationalUnit = GetProp(item, "Subject_OU"),
                Country            = GetProp(item, "Subject_C"),
            };
            int validityDays;
            if (!int.TryParse(GetProp(item, "ValidityDays"), out validityDays) || validityDays <= 0)
                validityDays = RolePresets.For(RolePreset).DefaultValidityDays;

            var keyAlg = KeyPairFactory.Parse(GetProp(item, "KeyAlgorithmRequest"));
            var keyPair = KeyPairFactory.Generate(keyAlg);

            var sans = new List<SanEntry>();
            var sanCsv = GetProp(item, "SubjectAlternativeNames");
            if (!string.IsNullOrEmpty(sanCsv))
            {
                foreach (var entry in sanCsv.Split('|'))
                {
                    var t = entry.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    if (t.StartsWith("DNS:", StringComparison.OrdinalIgnoreCase))
                        sans.Add(SanEntry.Dns(t.Substring(4)));
                    else if (t.StartsWith("IP:", StringComparison.OrdinalIgnoreCase))
                        sans.Add(SanEntry.Ip(t.Substring(3)));
                }
            }

            var build = new CertBuildRequest
            {
                Role = RolePreset,
                Subject = subject,
                SubjectAlternativeNames = sans,
                NotBefore = DateTime.UtcNow.AddMinutes(-5),
                NotAfter  = DateTime.UtcNow.AddDays(validityDays),
                SubjectKeyPair = keyPair,
            };

            // For non-Root, load the issuer's PFX from its MIP item and feed
            // its cert + private key into the build.
            if (RolePreset != RolePreset.RootCA)
            {
                var issuerThumb = GetProp(item, "IssuerThumbprint");
                if (string.IsNullOrEmpty(issuerThumb))
                    throw new InvalidOperationException("Pick an issuing CA before saving.");

                var issuerItem = FindCertItemByThumbprint(issuerThumb);
                if (issuerItem == null)
                    throw new InvalidOperationException("Selected issuing CA was not found.");

                var pfxB64 = GetProp(issuerItem, "Pfx");
                if (string.IsNullOrEmpty(pfxB64))
                    throw new InvalidOperationException("Issuing CA has no private key on this machine; pick another or re-issue it.");

                var pfx = CertVault.FromBase64(pfxB64);
                var loaded = Pkcs12Bundle.Load(pfx, "");
                if (loaded.PrivateKey == null)
                    throw new InvalidOperationException("Issuing CA's PFX has no private key.");

                build.IssuerCert = loaded.Certificate;
                build.IssuerPrivateKey = loaded.PrivateKey;
                build.IssuerSignatureAlgorithm = "SHA256WITHRSA"; // matches the issuer's public-key family
            }

            var cert = CertBuilder.Build(build);

            // Bundle cert + key (+ chain extras when we have an issuer) and
            // base64-encode for MIP property storage.
            var chainExtras = new List<Org.BouncyCastle.X509.X509Certificate>();
            if (build.IssuerCert != null) chainExtras.Add(build.IssuerCert);

            var pfxBytes = Pkcs12Bundle.Build(cert, keyPair.Private, chainExtras, "", item.Name);
            item.Properties["Pfx"] = CertVault.ToBase64(pfxBytes);

            // Stash metadata on the MIP item.
            item.Properties["Thumbprint"]   = ToHex(GetThumbprint(cert));
            item.Properties["Subject"]      = cert.SubjectDN.ToString();
            item.Properties["Issuer"]       = cert.IssuerDN.ToString();
            item.Properties["SerialNumber"] = cert.SerialNumber.ToString(16).ToUpperInvariant();
            item.Properties["NotBefore"]    = cert.NotBefore.ToUniversalTime().ToString("o");
            item.Properties["NotAfter"]     = cert.NotAfter.ToUniversalTime().ToString("o");
            item.Properties["KeyAlgorithm"] = KeyPairFactory.Label(keyPair.Public);
            item.Properties["HasPrivateKey"] = "True";

            PKIDefinition.Log.Info($"Issued cert: {cert.SubjectDN} (thumbprint {item.Properties["Thumbprint"]})");
        }

        // ── Helpers ──────────────────────────────────────────────────────

        internal static string GetProp(Item item, string key)
            => item.Properties.ContainsKey(key) ? item.Properties[key] : "";

        // Walk all PKI item kinds to find a cert by its thumbprint.
        internal static Item FindCertItemByThumbprint(string thumbprint)
        {
            foreach (var kind in AllCertKinds())
            {
                var items = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                if (items == null) continue;
                foreach (var i in items)
                    if (string.Equals(GetProp(i, "Thumbprint"), thumbprint, StringComparison.OrdinalIgnoreCase))
                        return i;
            }
            return null;
        }

        internal static List<Item> FindAllCAs()
        {
            var result = new List<Item>();
            foreach (var kind in new[] { PKIDefinition.PkiRootCertKindId, PKIDefinition.PkiIntermediateKindId })
            {
                var items = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                if (items != null) result.AddRange(items);
            }
            return result;
        }

        // Walks every PKI kind and returns true if any item other than
        // `excluding` already uses `name` (case-insensitive). Used by
        // ValidateAndSaveUserControl to block duplicates.
        internal static bool IsDuplicateName(string name, Item excluding)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var kind in AllCertKinds())
            {
                var items = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                if (items == null) continue;
                foreach (var i in items)
                {
                    if (excluding != null
                        && i.FQID != null && excluding.FQID != null
                        && i.FQID.ObjectId == excluding.FQID.ObjectId) continue;
                    if (string.Equals((i.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        internal static List<string> FindCertsIssuedBy(string issuerThumbprint)
        {
            var hits = new List<string>();
            foreach (var kind in AllCertKinds())
            {
                var items = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                if (items == null) continue;
                foreach (var i in items)
                    if (string.Equals(GetProp(i, "IssuerThumbprint"), issuerThumbprint, StringComparison.OrdinalIgnoreCase))
                        hits.Add(i.Name);
            }
            return hits;
        }

        private static IEnumerable<Guid> AllCertKinds()
        {
            yield return PKIDefinition.PkiRootCertKindId;
            yield return PKIDefinition.PkiIntermediateKindId;
            yield return PKIDefinition.PkiHttpsKindId;
            yield return PKIDefinition.PkiDot1xKindId;
            yield return PKIDefinition.PkiServiceKindId;
        }

        private static byte[] GetThumbprint(Org.BouncyCastle.X509.X509Certificate cert)
        {
            var digest = new Org.BouncyCastle.Crypto.Digests.Sha1Digest();
            var encoded = cert.GetEncoded();
            digest.BlockUpdate(encoded, 0, encoded.Length);
            var hash = new byte[digest.GetDigestSize()];
            digest.DoFinal(hash, 0);
            return hash;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
