using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;

namespace BarcodeReader.Admin
{
    /// <summary>
    /// ItemManager for the "QR Codes" node.
    ///
    /// Owns two responsibilities that don't fit naturally on the per-channel manager:
    ///   1. CRUD for QR Code library items (payload text + notes + error-correction level).
    ///   2. Rules-engine event registration for BOTH "Barcode Detected" (source = channel)
    ///      and "QR Code Matched" (source = QR item). Milestone collects events from any
    ///      ItemManager in the plugin, so centralising them here keeps the channel manager
    ///      focused on scanner configuration.
    ///
    /// Duplicate-payload validation lives in ValidateAndSaveUserControl - saving is refused
    /// if another item already uses the same payload, matching the user's "this should not
    /// be possible" constraint.
    /// </summary>
    public class QRCodeItemManager : ItemManager
    {
        private QRCodeConfigUserControl _userControl;
        private readonly Guid _kind;

        public QRCodeItemManager(Guid kind) { _kind = kind; }

        public override void Init() { }
        public override void Close() => ReleaseUserControl();

        #region Event / State registration (Rules engine)

        public override Collection<VideoOS.Platform.Data.EventGroup> GetKnownEventGroups(CultureInfo culture)
        {
            return new Collection<VideoOS.Platform.Data.EventGroup>
            {
                new VideoOS.Platform.Data.EventGroup
                {
                    ID = BarcodeReaderDefinition.EventGroupId,
                    Name = "Barcode Reader"
                }
            };
        }

        public override Collection<VideoOS.Platform.Data.EventType> GetKnownEventTypes(CultureInfo culture)
        {
            // "Barcode Detected" is scoped to channel items (one event per post-debounce
            // decode, regardless of what was decoded). "QR Code Matched" is scoped to QR
            // items and only fires when the decoded text matches the item's Payload.
            return new Collection<VideoOS.Platform.Data.EventType>
            {
                new VideoOS.Platform.Data.EventType
                {
                    ID = BarcodeReaderDefinition.EventBarcodeDetectedId,
                    Message = "Barcode Detected",
                    GroupID = BarcodeReaderDefinition.EventGroupId,
                    StateGroupID = BarcodeReaderDefinition.StateGroupId,
                    State = "Detected",
                    DefaultSourceKind = BarcodeReaderDefinition.PluginKindId,
                    SourceKinds = new List<Guid> { BarcodeReaderDefinition.PluginKindId }
                },
                new VideoOS.Platform.Data.EventType
                {
                    ID = BarcodeReaderDefinition.EventQRCodeMatchedId,
                    Message = "QR Code Matched",
                    GroupID = BarcodeReaderDefinition.EventGroupId,
                    StateGroupID = BarcodeReaderDefinition.StateGroupId,
                    State = "Matched",
                    DefaultSourceKind = BarcodeReaderDefinition.QRCodeKindId,
                    SourceKinds = new List<Guid> { BarcodeReaderDefinition.QRCodeKindId }
                }
            };
        }

        public override Collection<VideoOS.Platform.Data.StateGroup> GetKnownStateGroups(CultureInfo culture)
        {
            return new Collection<VideoOS.Platform.Data.StateGroup>
            {
                new VideoOS.Platform.Data.StateGroup
                {
                    ID = BarcodeReaderDefinition.StateGroupId,
                    Name = "Barcode Reader Status",
                    States = new[] { "Idle", "Detected", "Matched" }
                }
            };
        }

        #endregion

        #region User Control lifecycle

        public override UserControl GenerateDetailUserControl()
        {
            _userControl = new QRCodeConfigUserControl();
            _userControl.ConfigurationChangedByUser += ConfigurationChangedByUserHandler;
            return _userControl;
        }

        public override void ReleaseUserControl()
        {
            if (_userControl != null)
                _userControl.ConfigurationChangedByUser -= ConfigurationChangedByUserHandler;
            _userControl = null;
        }

        public override void FillUserControl(Item item)
        {
            CurrentItem = item;
            _userControl?.FillContent(item);
        }

        public override void ClearUserControl()
        {
            CurrentItem = null;
            _userControl?.ClearContent();
        }

        public override bool ValidateAndSaveUserControl()
        {
            if (CurrentItem == null || _userControl == null) return true;

            var payload = _userControl.GetPayload();
            var error = _userControl.ValidateInput();
            if (error == null)
            {
                // Uniqueness check: payload must not be used by any OTHER item (matching
                // the "not allowed to save duplicates" constraint). Comparing on the saved
                // Payload property from Configuration  not CurrentItem.Properties which
                // still reflects the latest edits we're about to commit.
                var conflict = FindItemWithSamePayload(payload, exceptFqid: CurrentItem.FQID.ObjectId);
                if (conflict != null)
                {
                    error = $"Payload is already used by '{conflict.Name}'. Each QR Code must have a unique payload.";
                }
            }

            if (error != null)
            {
                MessageBox.Show(error, "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _userControl.UpdateItem(CurrentItem);
            Configuration.Instance.SaveItemConfiguration(BarcodeReaderDefinition.PluginId, CurrentItem);
            return true;
        }

        private Item FindItemWithSamePayload(string payload, Guid exceptFqid)
        {
            if (string.IsNullOrEmpty(payload)) return null;
            var all = Configuration.Instance.GetItemConfigurations(
                BarcodeReaderDefinition.PluginId, null, _kind);
            return all.FirstOrDefault(i =>
                i.FQID.ObjectId != exceptFqid &&
                i.Properties.ContainsKey(QRCodeConfig.KeyPayload) &&
                string.Equals(i.Properties[QRCodeConfig.KeyPayload], payload, StringComparison.Ordinal));
        }

        public override OperationalState GetOperationalState(Item item) => OperationalState.Ok;

        public override string GetItemStatusDetails(Item item, string language)
        {
            if (item == null) return "";
            return item.Properties.ContainsKey(QRCodeConfig.KeyPayload)
                ? $"Payload: {Truncate(item.Properties[QRCodeConfig.KeyPayload], 60)}"
                : "(no payload)";
        }

        private static string Truncate(string s, int max)
            => s == null ? "" : (s.Length > max ? s.Substring(0, max - 3) + "..." : s);

        #endregion

        #region CRUD

        public override string GetItemName() => _userControl?.DisplayName ?? (CurrentItem?.Name ?? "");
        public override void SetItemName(string name) { if (CurrentItem != null) CurrentItem.Name = name; }

        public override List<Item> GetItems()
            => Configuration.Instance.GetItemConfigurations(BarcodeReaderDefinition.PluginId, null, _kind);

        public override List<Item> GetItems(Item parentItem)
            => Configuration.Instance.GetItemConfigurations(BarcodeReaderDefinition.PluginId, parentItem, _kind);

        public override Item GetItem(FQID fqid)
            => Configuration.Instance.GetItemConfiguration(BarcodeReaderDefinition.PluginId, _kind, fqid.ObjectId);

        public override Item CreateItem(Item parentItem, FQID suggestedFQID)
        {
            CurrentItem = new Item(suggestedFQID, "New QR Code");
            CurrentItem.Properties[QRCodeConfig.KeyPayload] = "";
            CurrentItem.Properties[QRCodeConfig.KeyErrorCorrection] = QRCodeConfig.DefaultErrorCorrection;

            _userControl?.FillContent(CurrentItem);
            Configuration.Instance.SaveItemConfiguration(BarcodeReaderDefinition.PluginId, CurrentItem);
            return CurrentItem;
        }

        public override void DeleteItem(Item item)
        {
            if (item != null)
                Configuration.Instance.DeleteItemConfiguration(BarcodeReaderDefinition.PluginId, item);
        }

        #endregion
    }

    /// <summary>
    /// Property-key constants + defaults for QR Code items. Mirrors ChannelConfig; kept in
    /// its own file so the background plugin and UI share one source of truth.
    /// </summary>
    internal static class QRCodeConfig
    {
        public const string KeyPayload          = "Payload";
        public const string KeyErrorCorrection  = "ErrorCorrection"; // "L" | "M" | "Q" | "H"

        public const string DefaultErrorCorrection = "M";
    }
}
