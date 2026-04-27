using System.Reflection;
using System.Windows.Forms;
using VideoOS.Platform.Admin;

namespace CommunitySDK
{
    // ItemNodeUserControl variant of HtmlHelpUserControl. ItemManager.
    // GenerateOverviewUserControl() is typed to ItemNodeUserControl, so a
    // plain HtmlHelpUserControl can't be returned directly - this class
    // hosts the same WebBrowser-backed help page inside an
    // ItemNodeUserControl and is what each PKI ItemManager hands back when
    // the admin clicks the folder node in the Mgmt Client tree.
    public class HtmlHelpItemNodeUserControl : ItemNodeUserControl
    {
        public HtmlHelpItemNodeUserControl(Assembly pluginAssembly, string subfolder, string fileName)
        {
            var inner = new HtmlHelpUserControl(pluginAssembly, subfolder, fileName)
            {
                Dock = DockStyle.Fill,
            };
            Controls.Add(inner);
        }
    }
}
