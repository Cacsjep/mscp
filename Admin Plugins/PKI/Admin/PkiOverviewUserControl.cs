using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PKI.Crypto;
using VideoOS.Platform;

namespace PKI.Admin
{
    // The Overview pane: lists every cert across the five leaf folders
    // in a single sortable, searchable, filterable grid. The action bar
    // has Import / Generate-services / Refresh / Export / Delete; below
    // it sits a filter row (search + folder + issuer dropdowns).
    public class PkiOverviewUserControl : UserControl
    {
        private readonly Label _heading = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 80, 120),
            Margin = new Padding(0, 0, 0, 4),
        };
        private readonly Label _subHeading = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(110, 110, 110),
            Margin = new Padding(0, 0, 0, 12),
        };
        private readonly Button _import      = new Button { Text = "Import certificate...",            Width = 200, Height = 32 };
        private readonly Button _genServices = new Button { Text = "Generate service certificates...", Width = 240, Height = 32 };
        private readonly Button _autoSetup   = new Button { Text = "Auto setup",                       Width = 110, Height = 32 };
        private readonly Button _export      = new Button { Text = "Export...",                        Width = 100, Height = 32, Enabled = false };
        private readonly Button _delete      = new Button { Text = "Delete",                           Width = 90,  Height = 32, Enabled = false };
        private readonly Button _refresh     = new Button { Text = "Refresh",                          Width = 100, Height = 32 };

        // Filter row controls.
        private readonly TextBox  _search       = new TextBox  { Width = 260 };
        private readonly ComboBox _folderFilter = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _issuerFilter = new ComboBox { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };

        private ContextMenuStrip _exportMenu;
        private readonly DataGridView _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = SystemColors.Control,
            BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };

        // Master list - everything matching the five leaf kinds. Filter
        // controls narrow this into the visible grid rows; storing the
        // master separately means switching filters is a re-render only,
        // no second config-API roundtrip.
        private readonly List<Item> _allItems = new List<Item>();
        private readonly Dictionary<Item, Guid>   _kindByItem  = new Dictionary<Item, Guid>();
        private readonly Dictionary<Item, string> _labelByItem = new Dictionary<Item, string>();

        public PkiOverviewUserControl()
        {
            Dock = DockStyle.Fill;
            AutoScroll = false;
            BuildLayout();
            BuildGridColumns();
            BuildExportMenu();
            BuildFolderFilter();
            _import.Click       += (s, e) => OnImportClick();
            _genServices.Click  += (s, e) => OnGenerateServiceClick();
            _autoSetup.Click    += (s, e) => OnAutoSetupClick();
            _export.Click       += (s, e) => OnExportClick();
            _delete.Click       += (s, e) => OnDeleteClick();
            _refresh.Click      += (s, e) => Refresh();
            _grid.SelectionChanged += (s, e) => UpdateRowActions();
            _search.TextChanged              += (s, e) => RenderRows();
            _folderFilter.SelectedIndexChanged += (s, e) => RenderRows();
            _issuerFilter.SelectedIndexChanged += (s, e) => RenderRows();
        }

        private void BuildLayout()
        {
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 4,
                Padding = new Padding(20, 16, 20, 16),
            };
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // header
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // actions
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // filters
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // grid

            _heading.Text = "Certificate vault";
            top.Controls.Add(_heading, 0, 0);

            // Header strip: subheading on the left, action buttons on the right.
            var headerStrip = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, AutoSize = true,
            };
            headerStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            headerStrip.Controls.Add(_subHeading, 0, 0);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            };
            _import.Margin       = new Padding(0, 0, 8, 0);
            _genServices.Margin  = new Padding(0, 0, 8, 0);
            _autoSetup.Margin    = new Padding(0, 0, 8, 0);
            _export.Margin       = new Padding(0, 0, 8, 0);
            _delete.Margin       = new Padding(0, 0, 8, 0);
            _refresh.Margin      = new Padding(0, 0, 0, 0);
            actions.Controls.Add(_import);
            actions.Controls.Add(_genServices);
            actions.Controls.Add(_autoSetup);
            actions.Controls.Add(_export);
            actions.Controls.Add(_delete);
            actions.Controls.Add(_refresh);
            headerStrip.Controls.Add(actions, 1, 0);

            top.Controls.Add(headerStrip, 0, 1);

            // Filter row: search | folder | issuer.
            var filterStrip = new FlowLayoutPanel
            {
                AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Margin = new Padding(0, 8, 0, 8),
            };
            void AddLabel(string text)
            {
                filterStrip.Controls.Add(new Label
                {
                    Text = text, AutoSize = true, ForeColor = Color.FromArgb(110, 110, 110),
                    Margin = new Padding(0, 6, 4, 0),
                });
            }
            AddLabel("Search");
            _search.Margin = new Padding(0, 3, 18, 0);
            filterStrip.Controls.Add(_search);
            AddLabel("Folder");
            _folderFilter.Margin = new Padding(0, 3, 18, 0);
            filterStrip.Controls.Add(_folderFilter);
            AddLabel("Issuer");
            _issuerFilter.Margin = new Padding(0, 3, 0, 0);
            filterStrip.Controls.Add(_issuerFilter);
            top.Controls.Add(filterStrip, 0, 2);

            top.Controls.Add(_grid, 0, 3);

            Controls.Add(top);
        }

        private void BuildFolderFilter()
        {
            _folderFilter.Items.Clear();
            _folderFilter.Items.Add("All folders");
            foreach (var (_, label) in EnumerateLeafKinds())
                _folderFilter.Items.Add(label);
            _folderFilter.SelectedIndex = 0;
        }

        private void BuildGridColumns()
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Display name", Name = "Display", FillWeight = 200, MinimumWidth = 160,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Folder", Name = "Folder", FillWeight = 130, MinimumWidth = 130,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Issuer", Name = "Issuer", FillWeight = 200, MinimumWidth = 160,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Valid until", Name = "NotAfter", FillWeight = 110, MinimumWidth = 110,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Remaining", Name = "Remaining", FillWeight = 100, MinimumWidth = 100,
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Have Private Key", Name = "HasKey", FillWeight = 90, MinimumWidth = 100,
            });
        }

        // ── Data load ────────────────────────────────────────────────────

        public new void Refresh()
        {
            try
            {
                _allItems.Clear();
                _kindByItem.Clear();
                _labelByItem.Clear();

                foreach (var (kind, label) in EnumerateLeafKinds())
                {
                    var items = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                    if (items == null) continue;
                    foreach (var item in items)
                    {
                        if (string.IsNullOrEmpty(GetProp(item, "Thumbprint"))) continue;
                        _allItems.Add(item);
                        _kindByItem[item] = kind;
                        _labelByItem[item] = label;
                    }
                }

                BuildIssuerFilter();
                RenderRows();
            }
            catch (Exception ex)
            {
                _subHeading.Text = "Failed to load: " + ex.Message;
                _subHeading.ForeColor = Color.FromArgb(160, 30, 30);
            }
        }

        // Populate the issuer dropdown from the unique set of issuers
        // present across all loaded items, plus an "All" entry. Preserves
        // current selection if the same value is still in the list.
        private void BuildIssuerFilter()
        {
            var prev = _issuerFilter.SelectedItem as string;
            _issuerFilter.Items.Clear();
            _issuerFilter.Items.Add("All issuers");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _allItems)
            {
                var iss = FriendlyIssuer(item);
                if (string.IsNullOrEmpty(iss)) continue;
                if (seen.Add(iss)) _issuerFilter.Items.Add(iss);
            }
            int idx = 0;
            if (!string.IsNullOrEmpty(prev))
            {
                int p = _issuerFilter.Items.IndexOf(prev);
                if (p >= 0) idx = p;
            }
            _issuerFilter.SelectedIndex = idx;
        }

        // Apply the current filters and repaint the grid. Called by
        // Refresh(), the search box, and the two filter dropdowns.
        private void RenderRows()
        {
            _grid.Rows.Clear();
            int total = 0, expired = 0, soon = 0, noKey = 0;
            int shown = 0;

            var query = (_search.Text ?? "").Trim();
            var folderSel = _folderFilter.SelectedItem as string ?? "All folders";
            var issuerSel = _issuerFilter.SelectedItem as string ?? "All issuers";

            foreach (var item in _allItems)
            {
                total++;
                var status = ComputeStatus(GetProp(item, "NotAfter"));
                if (status == "Expired") expired++;
                else if (status == "Expires soon") soon++;
                var hasKey = string.Equals(GetProp(item, "HasPrivateKey"), "True", StringComparison.OrdinalIgnoreCase);
                if (!hasKey) noKey++;

                var label  = _labelByItem.TryGetValue(item, out var l) ? l : "";
                var issuer = FriendlyIssuer(item);

                if (folderSel != "All folders" && !string.Equals(label, folderSel, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (issuerSel != "All issuers" && !string.Equals(issuer, issuerSel, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (query.Length > 0 && !MatchesQuery(item, label, issuer, query))
                    continue;

                var rowIdx = _grid.Rows.Add(
                    item.Name,
                    label,
                    issuer,
                    FormatDate(GetProp(item, "NotAfter")),
                    FormatRemaining(GetProp(item, "NotAfter")),
                    hasKey ? "yes" : "no");
                var row = _grid.Rows[rowIdx];
                row.Tag = new RowItem { Item = item, Kind = _kindByItem.TryGetValue(item, out var k) ? k : Guid.Empty };

                if (status == "Expired")
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
                else if (status == "Expires soon")
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
                shown++;
            }

            _subHeading.Text = $"{shown} of {total} certificate{(total == 1 ? "" : "s")} shown. "
                             + $"{expired} expired, {soon} expiring within 30 days, {noKey} without private key.";
            _subHeading.ForeColor = Color.FromArgb(110, 110, 110);
        }

        private static bool MatchesQuery(Item item, string label, string issuer, string query)
        {
            bool Match(string s) => !string.IsNullOrEmpty(s)
                && s.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            return Match(item.Name)
                || Match(label)
                || Match(issuer)
                || Match(GetProp(item, "Subject"))
                || Match(GetProp(item, "Subject_CN"))
                || Match(GetProp(item, "Thumbprint"))
                || Match(GetProp(item, "SubjectAlternativeNames"))
                || Match(GetProp(item, "SerialNumber"));
        }

        private static string ComputeStatus(string notAfterIso)
        {
            if (string.IsNullOrEmpty(notAfterIso)) return "?";
            if (!DateTime.TryParse(notAfterIso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var na)) return "?";
            var now = DateTime.UtcNow;
            if (na < now) return "Expired";
            if (na < now.AddDays(30)) return "Expires soon";
            return "OK";
        }

        private static string FormatDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt)) return iso;
            return dt.ToUniversalTime().ToString("yyyy-MM-dd");
        }

        // Remaining days. Negative values mean already-expired.
        private static string FormatRemaining(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt)) return "";
            var days = (int)Math.Round((dt.ToUniversalTime() - DateTime.UtcNow).TotalDays);
            if (days < 0) return $"expired {(-days)}d ago";
            if (days == 0) return "expires today";
            if (days == 1) return "1 day";
            if (days < 365) return days + " days";
            int years = days / 365;
            int rem = days % 365;
            return rem == 0 ? years + "y" : $"{years}y {rem}d";
        }

        private static IEnumerable<(Guid kind, string label)> EnumerateLeafKinds()
        {
            yield return (PKIDefinition.PkiRootCertKindId,     "Root CA");
            yield return (PKIDefinition.PkiIntermediateKindId, "Intermediate CA");
            yield return (PKIDefinition.PkiHttpsKindId,        "HTTPS");
            yield return (PKIDefinition.PkiDot1xKindId,        "802.1X");
            yield return (PKIDefinition.PkiServiceKindId,      "Service");
        }

        private static string GetProp(Item item, string key)
            => (item != null && item.Properties.ContainsKey(key)) ? item.Properties[key] : "";

        // Short, human-readable issuer for the grid. Prefers the Display
        // Name of the issuing item (when the issuer is one of our own CAs);
        // otherwise falls back to the CN value pulled from the Issuer DN.
        // Self-signed certs render as "self-signed" so admins can spot Roots
        // at a glance.
        private static string FriendlyIssuer(Item item)
        {
            var issuerDn = GetProp(item, "Issuer");
            var subjectDn = GetProp(item, "Subject");
            if (!string.IsNullOrEmpty(issuerDn) && issuerDn == subjectDn) return "self-signed";

            var thumb = GetProp(item, "IssuerThumbprint");
            if (!string.IsNullOrEmpty(thumb))
            {
                foreach (var (kind, _) in EnumerateLeafKinds())
                {
                    var candidates = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                    if (candidates == null) continue;
                    foreach (var cand in candidates)
                    {
                        if (string.Equals(GetProp(cand, "Thumbprint"), thumb, StringComparison.OrdinalIgnoreCase))
                            return cand.Name;
                    }
                }
            }
            return ExtractCn(issuerDn);
        }

        private static string ExtractCn(string dn)
        {
            if (string.IsNullOrEmpty(dn)) return "";
            foreach (var part in dn.Split(','))
            {
                var p = part.Trim();
                if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return p.Substring(3).Trim();
            }
            return dn;
        }

        // ── Actions ──────────────────────────────────────────────────────

        private void OnImportClick()
        {
            using (var dlg = new PkiImportDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    Refresh();
            }
        }

        private void OnGenerateServiceClick()
        {
            using (var dlg = new PkiServiceCertDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    Refresh();
            }
        }

        // Zero-knowledge "Auto setup" path: drops a single self-signed
        // 15-year Root CA and one 15-year Service certificate per
        // discovered XProtect service. Each service cert's SAN list is
        // populated from ServiceDiscovery (hostname + FQDN + IPs the
        // service registered with). Existing items with the same
        // generated names are left alone so re-clicking the button is
        // safe.
        private void OnAutoSetupClick()
        {
            const string rootName = "MSCP Auto-Setup Root CA";
            const int fifteenYears = 15 * 365;

            var confirm = MessageBox.Show(
                "Auto setup will:\n\n" +
                "  - Generate a Root CA \"" + rootName + "\" valid for 15 years\n" +
                "    (skipped if it already exists).\n" +
                "  - Generate a Service certificate for every XProtect\n" +
                "    service detected on this site, valid for 15 years,\n" +
                "    signed by the Root CA, with hostname / FQDN / IP\n" +
                "    SANs filled in automatically.\n" +
                "  - Existing certificates with the same names are NOT\n" +
                "    overwritten.\n\n" +
                "Proceed?",
                "Auto setup",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                // ── Root CA ──
                var root = FindByName(rootName);
                bool rootCreated = false;
                if (root == null)
                {
                    root = CreateAutoSetupRoot(rootName, fifteenYears);
                    if (root == null)
                    {
                        MessageBox.Show("Failed to generate the Root CA. Check MIPLog for details.",
                            "Auto setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    rootCreated = true;
                }

                // ── Discover services ──
                var services = ServiceDiscovery.Discover();
                if (services == null || services.Count == 0)
                {
                    MessageBox.Show(
                        "Root CA " + (rootCreated ? "created" : "found") + ".\n\n" +
                        "No XProtect services were detected on this site, so no service\n" +
                        "certificates were generated. Add servers to the Mgmt Client and\n" +
                        "click Auto setup again.",
                        "Auto setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Refresh();
                    return;
                }

                // ── Per-service leaf certs ──
                int created = 0, skipped = 0, failed = 0;
                var details = new System.Text.StringBuilder();
                foreach (var svc in services)
                {
                    var name = "Auto-Setup " + svc.Prefix + " " + (svc.DisplayName ?? svc.Hostname ?? "service");
                    if (FindByName(name) != null)
                    {
                        skipped++;
                        details.AppendLine("SKIP " + name + " (already exists)");
                        continue;
                    }

                    var dnsNames = new List<string>();
                    if (!string.IsNullOrEmpty(svc.Hostname)) dnsNames.Add(svc.Hostname);
                    if (!string.IsNullOrEmpty(svc.Fqdn)
                        && !svc.Fqdn.Equals(svc.Hostname, StringComparison.OrdinalIgnoreCase))
                        dnsNames.Add(svc.Fqdn);

                    var req = new ServiceCertRequest
                    {
                        DestinationKind    = PKIDefinition.PkiServiceKindId,
                        Role               = RolePreset.Service,
                        ItemName           = name,
                        CommonName         = svc.Hostname ?? svc.DisplayName ?? Environment.MachineName,
                        Organization       = "MSCP",
                        OrganizationalUnit = svc.CategoryLabel,
                        Country            = "",
                        IssuingCA          = root,
                        ValidityDays       = fifteenYears,
                        KeyAlgorithm       = KeyAlgorithm.Rsa2048,
                        DnsNames           = dnsNames,
                        IpAddresses        = svc.IpAddresses ?? new List<string>(),
                    };

                    var result = ServiceCertIssuer.Issue(req);
                    if (result != null && result.Ok)
                    {
                        created++;
                        details.AppendLine("OK   " + name);
                    }
                    else
                    {
                        failed++;
                        details.AppendLine("FAIL " + name + ": " + (result?.Message ?? "(unknown error)"));
                    }
                }

                MessageBox.Show(
                    "Auto setup complete.\n\n" +
                    "Root CA: " + (rootCreated ? "created" : "reused") + " (\"" + rootName + "\")\n" +
                    "Service certificates created: " + created + "\n" +
                    "Skipped (already existed):    " + skipped + "\n" +
                    "Failed:                       " + failed + "\n\n" +
                    "Details:\n" + details,
                    "Auto setup",
                    MessageBoxButtons.OK,
                    failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                Refresh();
            }
            catch (Exception ex)
            {
                PKIDefinition.Log.Error("Auto setup failed: " + ex);
                MessageBox.Show("Auto setup failed:\n\n" + ex.Message, "Auto setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        // Look up a cert item by display name across every PKI kind.
        // Returns null if no match.
        private static Item FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var (kind, _) in EnumerateLeafKinds())
            {
                var items = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, kind);
                if (items == null) continue;
                foreach (var i in items)
                {
                    if (string.Equals((i.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return null;
        }

        // Generate a self-signed Root CA item end-to-end. Same persistence
        // shape PkiCertItemManager.GenerateAndStore writes for normal
        // user-driven Root CA creates - so the auto-setup root behaves
        // exactly like a hand-rolled one in every other code path
        // (issuer dropdowns, deletion checks, REST mipItems output).
        private static Item CreateAutoSetupRoot(string rootName, int validityDays)
        {
            try
            {
                var keyPair = KeyPairFactory.Generate(KeyAlgorithm.Rsa2048);
                var build = new CertBuildRequest
                {
                    Role = RolePreset.RootCA,
                    Subject = new CertSubject
                    {
                        CommonName         = rootName,
                        Organization       = "MSCP",
                        OrganizationalUnit = "PKI",
                        Country            = "",
                    },
                    SubjectAlternativeNames = new List<SanEntry>(),
                    NotBefore = DateTime.UtcNow.AddMinutes(-5),
                    NotAfter  = DateTime.UtcNow.AddDays(validityDays),
                    SubjectKeyPair = keyPair,
                };
                var cert = CertBuilder.Build(build);
                var pfxBytes = Pkcs12Bundle.Build(cert, keyPair.Private,
                    new List<Org.BouncyCastle.X509.X509Certificate>(), "", rootName);

                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                var fqid = new FQID(serverId, Guid.Empty, Guid.NewGuid(),
                    FolderType.No, PKIDefinition.PkiRootCertKindId);
                var item = new Item(fqid, rootName);

                item.Properties["RolePreset"]          = "RootCA";
                item.Properties["ValidityDays"]        = validityDays.ToString();
                item.Properties["KeyAlgorithmRequest"] = "RSA-2048";
                item.Properties["Subject_CN"]          = rootName;
                item.Properties["Subject_O"]           = "MSCP";
                item.Properties["Subject_OU"]          = "PKI";
                item.Properties["Subject_C"]           = "";
                item.Properties["SubjectAlternativeNames"] = "";
                item.Properties["IssuerThumbprint"]    = "";
                item.Properties["CreatedAt"]           = DateTime.UtcNow.ToString("o");
                item.Properties["Pfx"]                 = Storage.CertVault.ToBase64(pfxBytes);
                item.Properties["Thumbprint"]          = ToHex(Sha1(cert.GetEncoded()));
                item.Properties["Subject"]             = cert.SubjectDN.ToString();
                item.Properties["Issuer"]              = cert.IssuerDN.ToString();
                item.Properties["SerialNumber"]        = cert.SerialNumber.ToString(16).ToUpperInvariant();
                item.Properties["NotBefore"]           = cert.NotBefore.ToUniversalTime().ToString("o");
                item.Properties["NotAfter"]            = cert.NotAfter.ToUniversalTime().ToString("o");
                item.Properties["KeyAlgorithm"]        = KeyPairFactory.Label(keyPair.Public);
                item.Properties["HasPrivateKey"]       = "True";

                Configuration.Instance.SaveItemConfiguration(PKIDefinition.PluginId, item);
                PKIDefinition.Log.Info("Auto-setup Root CA created: " + rootName);
                return item;
            }
            catch (Exception ex)
            {
                PKIDefinition.Log.Error("Auto-setup Root CA generation failed: " + ex);
                return null;
            }
        }

        private static byte[] Sha1(byte[] bytes)
        {
            var d = new Org.BouncyCastle.Crypto.Digests.Sha1Digest();
            d.BlockUpdate(bytes, 0, bytes.Length);
            var h = new byte[d.GetDigestSize()];
            d.DoFinal(h, 0);
            return h;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        private void UpdateRowActions()
        {
            var hasRow = _grid.SelectedRows.Count == 1
                && _grid.SelectedRows[0].Tag is RowItem;
            _delete.Enabled = hasRow;
            _export.Enabled = hasRow;
        }

        private void BuildExportMenu()
        {
            // One drop-down lists every export format. Key-bearing items
            // are surfaced regardless of HasPrivateKey; CertExporter
            // refuses with a friendly message for cert-only items.
            _exportMenu = new ContextMenuStrip();
            void Add(string text, string format)
            {
                var mi = new ToolStripMenuItem(text);
                mi.Click += (s, e) =>
                {
                    var ri = SelectedRowItem();
                    if (ri != null) CertExporter.ExportInteractive(ri.Item, format, this);
                };
                _exportMenu.Items.Add(mi);
            }
            Add("PEM (.pem) - cert only",         CertExporter.Format.PemCert);
            Add("DER (.der) - cert only",         CertExporter.Format.DerCert);
            Add(".crt - cert only",               CertExporter.Format.Crt);
            _exportMenu.Items.Add(new ToolStripSeparator());
            Add("PFX (cert + key)...",            CertExporter.Format.Pfx);
            Add("PEM PKCS#8 key...",              CertExporter.Format.PemKeyPkcs8);
            Add("PEM PKCS#1 RSA key (RSA only)",  CertExporter.Format.PemKeyPkcs1);
            Add("PEM bundle (cert + key)",        CertExporter.Format.PemBundle);
        }

        private void OnExportClick()
        {
            if (SelectedRowItem() == null) return;
            _exportMenu.Show(_export, new Point(0, _export.Height));
        }

        private RowItem SelectedRowItem()
        {
            if (_grid.SelectedRows.Count != 1) return null;
            return _grid.SelectedRows[0].Tag as RowItem;
        }

        private void OnDeleteClick()
        {
            if (_grid.SelectedRows.Count != 1) return;
            if (!(_grid.SelectedRows[0].Tag is RowItem ri)) return;

            var item = ri.Item;
            var thumb = GetProp(item, "Thumbprint");

            // Block delete if other certs reference this one as their issuer.
            // Same rule the per-folder ItemManager enforces; reproduced here
            // so admins get the same protection from the Overview pane.
            if (!string.IsNullOrEmpty(thumb))
            {
                var dependents = PkiCertItemManager.FindCertsIssuedBy(thumb);
                if (dependents.Count > 0)
                {
                    var names = string.Join("\n  - ", dependents);
                    MessageBox.Show(
                        "This certificate has issued other certificates and cannot be deleted.\n\n"
                        + "Dependent certificates:\n  - " + names
                        + "\n\nDelete or re-issue those first.",
                        "Delete blocked",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var confirm = MessageBox.Show(
                $"Delete certificate \"{item.Name}\"?\n\nThis removes the cert and its private key from this server.",
                "Delete certificate",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (confirm != DialogResult.OK) return;

            try
            {
                Configuration.Instance.DeleteItemConfiguration(PKIDefinition.PluginId, item);
                PKIDefinition.Log.Info($"Deleted cert from Overview: {item.Name} (thumbprint {thumb})");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Delete failed:\n\n" + ex.Message, "PKI",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Refresh();
        }

        private class RowItem
        {
            public Item Item;
            public Guid Kind;
        }
    }
}
