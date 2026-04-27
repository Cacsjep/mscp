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
    //
    // Dock = Fill on both the wrapper and the inner control because the
    // Mgmt Client just adds the returned control to its detail pane and
    // does NOT impose a layout - the default Size on a stock
    // ItemNodeUserControl is small (~150x150) which leaves the help text
    // crammed into a tiny column on the left of the pane otherwise.
    public class HtmlHelpItemNodeUserControl : ItemNodeUserControl
    {
        public HtmlHelpItemNodeUserControl(Assembly pluginAssembly, string subfolder, string fileName)
        {
            Dock = DockStyle.Fill;
            AutoScroll = false;
            var inner = new HtmlHelpUserControl(pluginAssembly, subfolder, fileName)
            {
                Dock = DockStyle.Fill,
            };
            Controls.Add(inner);
        }
    }
}
