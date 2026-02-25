using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RtmpStreamer.Admin
{
    public class HtmlHelpUserControl : UserControl
    {
        public HtmlHelpUserControl()
        {
            var browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                IsWebBrowserContextMenuEnabled = false,
                AllowNavigation = false,
                ScriptErrorsSuppressed = true
            };

            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var htmlPath = Path.Combine(dir, "Admin", "HelpPage.html");

            if (File.Exists(htmlPath))
                browser.Url = new System.Uri(htmlPath);
            else
                browser.DocumentText = "<html><body><p>Help page not found.</p></body></html>";

            Controls.Add(browser);
        }
    }
}
