using System;
using System.Collections.Generic;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace PKI.Admin
{
    // Empty ItemManager whose only job is to serve a static help page
    // when the admin clicks one of the GROUP folder nodes in the tree
    // (CA Certificates, Client Certificates). These nodes have
    // ItemsAllowed.None, so the Mgmt Client never asks us for items;
    // the only override that matters is GenerateOverviewUserControl,
    // which renders the embedded HelpPage_*.html for that group.
    public class HelpOnlyItemManager : ItemManager
    {
        private readonly string _helpFileName;
        public HelpOnlyItemManager(string helpFileName) { _helpFileName = helpFileName; }

        public override void Init() { }
        public override void Close() { }

        public override ItemNodeUserControl GenerateOverviewUserControl()
        {
            return new HtmlHelpItemNodeUserControl(
                System.Reflection.Assembly.GetExecutingAssembly(),
                "Admin", _helpFileName);
        }

        // No items live under a group node, so every CRUD method is a no-op.
        public override List<Item> GetItems() => new List<Item>();
        public override List<Item> GetItems(Item parentItem) => new List<Item>();
        public override Item GetItem(FQID fqid) => null;
        public override Item CreateItem(Item parentItem, FQID suggestedFQID) => null;
        public override void DeleteItem(Item item) { }

        public override System.Windows.Forms.UserControl GenerateDetailUserControl() => null;
        public override void FillUserControl(Item item) { }
        public override void ClearUserControl() { }
        public override void ReleaseUserControl() { }
        public override bool ValidateAndSaveUserControl() => true;
    }
}
