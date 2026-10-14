using System;
using System.Windows.Media;
using VideoOS.Platform;
using VideoOS.Platform.Messaging;
using VideoOS.Platform.UI.Controls;

namespace LowLatencyVideoWall.Admin.Wpf
{
    /// <summary>
    /// Shared helpers for the WPF admin console, ported from Race Commander: Milestone-styled
    /// confirmations, tree refresh after grid edits, and FQID minting for items created from
    /// a console page instead of the Management Client tree.
    /// </summary>
    internal static class AdminSupport
    {
        /// <summary>Ask the Management Client to redraw the config tree after a console-side save/delete.</summary>
        public static void RefreshTree(Guid kind)
        {
            EnvironmentManager.Instance.SendMessage(
                new Message(MessageId.System.ApplicationRefreshTreeViewCommand) { Data = kind });
        }

        /// <summary>Mint a new root-level item FQID of the given kind (the same shape the MC's
        /// suggestedFQID has for root ItemNodes: master server, no parent).</summary>
        public static FQID NewRootFqid(Guid kind)
        {
            return new FQID(EnvironmentManager.Instance.MasterSite.ServerId,
                Guid.Empty, Guid.NewGuid(), FolderType.No, kind);
        }

        /// <summary>Milestone-styled yes/no delete confirmation. Returns true if the user chose Yes.</summary>
        public static bool ConfirmDelete(string message)
        {
            var options = new VideoOSMessageBox.Options { YesButtonAppearance = VideoOSMessageBox.ButtonAppearances.Destructive };
            var result = VideoOSMessageBox.Show(
                owner: null,
                title: "Delete",
                header: "Delete this entry?",
                message: message,
                buttons: VideoOSMessageBox.Buttons.YesNo,
                defaultButton: VideoOSMessageBox.ResultButtons.No,
                icon: new VideoOSIconBuiltInSource { Icon = VideoOSIconBuiltInSource.Icons.Questionmark },
                showRememberChoiceCheckBox: false,
                options: options);
            return result.Button == VideoOSMessageBox.ResultButtons.Yes;
        }

        /// <summary>Milestone-styled destructive yes/no confirmation with a custom header. Returns true on Yes.</summary>
        public static bool ConfirmDestructive(string header, string message)
        {
            var options = new VideoOSMessageBox.Options { YesButtonAppearance = VideoOSMessageBox.ButtonAppearances.Destructive };
            var result = VideoOSMessageBox.Show(
                owner: null,
                title: "Delete",
                header: header,
                message: message,
                buttons: VideoOSMessageBox.Buttons.YesNo,
                defaultButton: VideoOSMessageBox.ResultButtons.No,
                icon: new VideoOSIconBuiltInSource { Icon = VideoOSIconBuiltInSource.Icons.Questionmark },
                showRememberChoiceCheckBox: false,
                options: options);
            return result.Button == VideoOSMessageBox.ResultButtons.Yes;
        }

        /// <summary>Navigate-away prompt when the panel has unsaved changes. Yes = save, No = discard.</summary>
        public static bool ConfirmSaveOnLeave(string message)
        {
            var result = VideoOSMessageBox.Show(
                owner: null,
                title: "Unsaved changes",
                header: "Save your changes?",
                message: message,
                buttons: VideoOSMessageBox.Buttons.YesNo,
                defaultButton: VideoOSMessageBox.ResultButtons.Yes,
                icon: new VideoOSIconBuiltInSource { Icon = VideoOSIconBuiltInSource.Icons.Questionmark },
                showRememberChoiceCheckBox: false,
                options: new VideoOSMessageBox.Options());
            return result.Button == VideoOSMessageBox.ResultButtons.Yes;
        }

        /// <summary>Count comma-separated entries in a CSV item property.</summary>
        public static int CsvCount(Item item, string key)
        {
            if (item == null || !item.Properties.ContainsKey(key) || string.IsNullOrEmpty(item.Properties[key]))
                return 0;
            return item.Properties[key].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
