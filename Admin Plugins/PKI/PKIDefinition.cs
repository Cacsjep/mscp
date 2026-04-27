using CommunitySDK;
using FontAwesome5;
using PKI.Admin;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VideoOS.Platform;
using VideoOS.Platform.Admin;
using VideoOS.Platform.Background;

namespace PKI
{
    public class PKIDefinition : PluginDefinition
    {
        internal static readonly Guid PluginId = new Guid("A6027637-6C03-4C58-A6DB-7B837C74AA60");

        // Top-level container kinds.
        internal static readonly Guid PkiCAFolderKindId      = new Guid("673BF7C2-098D-413C-98C1-6F31278F853A");
        internal static readonly Guid PkiClientFolderKindId  = new Guid("26071BB4-7CAD-486C-AAF4-2AA74FC74CAF");

        // Leaf kinds (each has its own ItemManager and "Add new" via right-click).
        internal static readonly Guid PkiRootCertKindId      = new Guid("25BAE827-E09F-48C5-BB14-2072EA0573C2");
        internal static readonly Guid PkiIntermediateKindId  = new Guid("1614CDD4-DBE4-44E7-AAF5-17D347087006");
        internal static readonly Guid PkiHttpsKindId         = new Guid("53EEFF60-02F5-40B4-9C9C-65DA698CBEDA");
        internal static readonly Guid PkiDot1xKindId         = new Guid("AE29EE2B-8D42-4A13-BE55-15DEA4C4D029");
        internal static readonly Guid PkiServiceKindId       = new Guid("3E264439-F043-4333-901C-951D5B5E5ACC");

        // Overview kind. Single auto-created item; clicking it shows a grid
        // of every certificate in the vault and a button to import new ones.
        internal static readonly Guid PkiOverviewKindId      = new Guid("1403A91F-CB8E-43B9-AF55-3E8B5F9F2966");

        private List<ItemNode> _itemNodes;

        private Image _pluginIcon = PluginIcon.FallbackIcon;
        private Image _folderIcon = PluginIcon.FallbackIcon;
        private Image _certIcon   = PluginIcon.FallbackIcon;
        private Image _caIcon     = PluginIcon.FallbackIcon;
        private Image _overviewIcon = PluginIcon.FallbackIcon;
        private Image _caFolderIcon = PluginIcon.FallbackIcon;
        private Image _clientFolderIcon = PluginIcon.FallbackIcon;

        internal static readonly PluginLog Log = new PluginLog("PKI");

        public override Guid Id => PluginId;
        public override string Name => "PKI";
        public override string Manufacturer => "https://github.com/Cacsjep";
        public override Image Icon => _pluginIcon ?? PluginIcon.FallbackIcon;

        public override void Init()
        {
            var env = EnvironmentManager.Instance.EnvironmentType;
            Log.Info($"PluginDefinition Init - environment: {env}");

            if (env != EnvironmentType.Service)
            {
                try
                {
                    _pluginIcon       = PluginIcon.Render(EFontAwesomeIcon.Solid_Key);
                    _folderIcon       = PluginIcon.Render(EFontAwesomeIcon.Solid_FolderOpen);
                    _certIcon         = PluginIcon.Render(EFontAwesomeIcon.Solid_Certificate);
                    _caIcon           = PluginIcon.Render(EFontAwesomeIcon.Solid_Stamp);
                    _overviewIcon     = PluginIcon.Render(EFontAwesomeIcon.Solid_ListAlt);
                    _caFolderIcon     = PluginIcon.Render(EFontAwesomeIcon.Solid_Landmark);
                    _clientFolderIcon = PluginIcon.Render(EFontAwesomeIcon.Solid_IdCard);
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to render FA icons: {ex.Message}");
                }
                _itemNodes = null;
            }
        }

        public override void Close()
        {
            _itemNodes = null;
        }

        // The Management Client wraps every plugin's ItemNodes under a top-level
        // node named after the plugin (here: "PKI"). Returning two top-level
        // groups (CA Certificates, Client Certificates) is enough - adding our
        // own "PKI" wrapper duplicates the auto-generated one.
        public override List<ItemNode> ItemNodes
        {
            get
            {
                var env = EnvironmentManager.Instance.EnvironmentType;
                if (env != EnvironmentType.Administration && env != EnvironmentType.Service)
                    return null;

                if (_itemNodes != null) return _itemNodes;

                var rootCertNode = new ItemNode(
                    PkiRootCertKindId, PkiCAFolderKindId,
                    "Root Certificate",  _caIcon,
                    "Root Certificates", _folderIcon,
                    Category.Text, true, ItemsAllowed.Many,
                    new RootCertItemManager(PkiRootCertKindId), null);

                var intermediateNode = new ItemNode(
                    PkiIntermediateKindId, PkiCAFolderKindId,
                    "Intermediate Certificate",  _caIcon,
                    "Intermediate Certificates", _folderIcon,
                    Category.Text, true, ItemsAllowed.Many,
                    new IntermediateCertItemManager(PkiIntermediateKindId), null);

                var caFolderNode = new ItemNode(
                    PkiCAFolderKindId, Guid.Empty,
                    "CA Certificates", _caFolderIcon,
                    "CA Certificates", _caFolderIcon,
                    Category.Text, false, ItemsAllowed.None,
                    new HelpOnlyItemManager("HelpPage_CA.html"),
                    new List<ItemNode> { rootCertNode, intermediateNode });

                var httpsNode = new ItemNode(
                    PkiHttpsKindId, PkiClientFolderKindId,
                    "HTTPS Certificate",  _certIcon,
                    "HTTPS Certificates", _folderIcon,
                    Category.Text, true, ItemsAllowed.Many,
                    new HttpsCertItemManager(PkiHttpsKindId), null);

                var dot1xNode = new ItemNode(
                    PkiDot1xKindId, PkiClientFolderKindId,
                    "802.1X Certificate",  _certIcon,
                    "802.1X Certificates", _folderIcon,
                    Category.Text, true, ItemsAllowed.Many,
                    new Dot1xCertItemManager(PkiDot1xKindId), null);

                var serviceNode = new ItemNode(
                    PkiServiceKindId, PkiClientFolderKindId,
                    "Service Certificate",  _certIcon,
                    "Service Certificates", _folderIcon,
                    Category.Text, true, ItemsAllowed.Many,
                    new ServiceCertItemManager(PkiServiceKindId), null);

                var clientFolderNode = new ItemNode(
                    PkiClientFolderKindId, Guid.Empty,
                    "Client Certificates", _clientFolderIcon,
                    "Client Certificates", _clientFolderIcon,
                    Category.Text, false, ItemsAllowed.None,
                    new HelpOnlyItemManager("HelpPage_Client.html"),
                    new List<ItemNode> { httpsNode, dot1xNode, serviceNode });

                var overviewNode = new ItemNode(
                    PkiOverviewKindId, Guid.Empty,
                    "Overview", _overviewIcon,
                    "Overview", _overviewIcon,
                    Category.Text, true, ItemsAllowed.One,
                    new OverviewItemManager(PkiOverviewKindId), null);

                _itemNodes = new List<ItemNode> { overviewNode, caFolderNode, clientFolderNode };
                return _itemNodes;
            }
        }

        // Plugin-level help. Returned UserControl is rendered when the
        // admin clicks the top-level "PKI" node in the Mgmt Client tree.
        // Per-folder help (Root CA / Intermediate / HTTPS / 802.1X / Service)
        // lives on each ItemManager via GenerateOverviewUserControl.
        public override UserControl GenerateUserControl()
        {
            return new HtmlHelpUserControl(
                System.Reflection.Assembly.GetExecutingAssembly(),
                "Admin", "HelpPage.html");
        }
    }
}
