using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace PKI.Admin
{
    // Singleton-style ItemManager: the Overview node holds exactly one item
    // (auto-created on first list / first click). CreateItem returns the
    // existing singleton instead of creating a new one. DeleteItem refuses.
    // Result: admin always sees one "Overview" entry under the Overview
    // folder; clicking it shows the grid + Import button via the user
    // control returned by GenerateDetailUserControl.
    public class OverviewItemManager : ItemManager
    {
        private readonly Guid _kind;

        // Stable ObjectId so the singleton is always the same item across
        // restarts and the MIP config doesn't accumulate copies.
        private static readonly Guid SingletonObjectId = new Guid("0F0E0D0C-0B0A-4509-8807-060504030201");

        public OverviewItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() { ReleaseUserControl(); }

        // ── User control ─────────────────────────────────────────────────

        private PkiOverviewUserControl _uc;

        public override UserControl GenerateDetailUserControl()
        {
            _uc = new PkiOverviewUserControl();
            return _uc;
        }

        public override void ReleaseUserControl() { _uc = null; }

        public override void FillUserControl(Item item)
        {
            CurrentItem = item;
            _uc?.Refresh();
        }

        public override void ClearUserControl()
        {
            CurrentItem = null;
        }

        public override bool ValidateAndSaveUserControl()
        {
            // Overview is read-only metadata; nothing to persist.
            return true;
        }

        // ── Singleton item plumbing ─────────────────────────────────────

        public override string GetItemName() => "Overview";

        public override void SetItemName(string name) { /* immutable */ }

        // The Overview node is visible if the role has read on at least
        // ONE of the five folders. The Overview UI then filters the cert
        // list per folder using the same per-folder action checks.
        public override List<Item> GetItems()
        {
            if (!PKIDefinition.HasAnyReadPermission()) return new List<Item>();
            var list = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, _kind);
            if (list == null || list.Count == 0)
            {
                EnsureSingleton();
                list = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, _kind);
            }
            return list ?? new List<Item>();
        }

        public override List<Item> GetItems(Item parentItem) => GetItems();

        public override Item GetItem(FQID fqid)
        {
            if (!PKIDefinition.HasAnyReadPermission()) return null;
            return Configuration.Instance.GetItemConfiguration(PKIDefinition.PluginId, _kind, fqid.ObjectId);
        }

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            // Refuse "Add new" for additional copies; just return the
            // existing singleton (or create it if missing).
            EnsureSingleton();
            var list = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, _kind);
            if (list != null && list.Count > 0)
            {
                CurrentItem = list[0];
                return CurrentItem;
            }
            // Fallback: shouldn't happen, but if EnsureSingleton failed,
            // accept the framework's suggested FQID.
            CurrentItem = new Item(suggestedFQID, "Overview");
            Configuration.Instance.SaveItemConfiguration(PKIDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            // The singleton can't be deleted; the Overview node is meant to
            // always exist while the plugin is loaded.
        }

        private void EnsureSingleton()
        {
            // The singleton item is created from the Management Client only.
            // The Event Server's Service env also sees this ItemNode, but
            // MasterSite.ServerId can't be resolved into LoginSettings
            // there, so we'd just log a noisy error every time the tree is
            // walked. Skip it - the Mgmt Client will create the item and
            // the Service env will then find it via GetItems normally.
            if (EnvironmentManager.Instance.EnvironmentType != EnvironmentType.Administration)
                return;

            var list = Configuration.Instance.GetItemConfigurations(PKIDefinition.PluginId, null, _kind);
            if (list != null && list.Count > 0) return;

            try
            {
                var serverId = EnvironmentManager.Instance.MasterSite.ServerId;
                var fqid = new FQID(serverId, Guid.Empty, SingletonObjectId, FolderType.No, _kind);
                var item = new Item(fqid, "Overview");
                item.Properties["Singleton"] = "true";
                Configuration.Instance.SaveItemConfiguration(PKIDefinition.PluginId, item);
            }
            catch (Exception ex)
            {
                PKIDefinition.Log.Error($"Overview EnsureSingleton failed: {ex.Message}");
            }
        }
    }
}
