using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CommunitySDK
{
    /// <summary>
    /// Reusable UserControl that renders an HTML help page using an embedded WebBrowser.
    /// Place a HelpPage.html file in the Admin/ subfolder of your plugin output directory.
    /// </summary>
    public class HtmlHelpUserControl : UserControl
    {
        public HtmlHelpUserControl() : this(Assembly.GetCallingAssembly(), "Admin", "HelpPage.html") { }

        public HtmlHelpUserControl(Assembly pluginAssembly, string subfolder, string fileName)
        {
            var browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                IsWebBrowserContextMenuEnabled = false,
                AllowNavigation = false,
                ScriptErrorsSuppressed = true
            };

            var dir = Path.GetDirectoryName(pluginAssembly.Location);
            var htmlPath = Path.Combine(dir, subfolder, fileName);

            if (File.Exists(htmlPath))
                browser.Url = new System.Uri(htmlPath);
            else
                browser.DocumentText = "<html><body><p>Help page not found.</p></body></html>";

            Controls.Add(browser);
        }
    }
}
