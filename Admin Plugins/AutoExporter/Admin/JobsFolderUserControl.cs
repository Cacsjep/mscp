using System.Drawing;
using System.Windows.Forms;

namespace AutoExporter.Admin
{
    public class JobsFolderUserControl : UserControl
    {
        public JobsFolderUserControl()
        {
            var lbl = new Label
            {
                Text = "Right-click \"Jobs\" → \"Create New…\" to add an export job.\n\n" +
                       "Each job specifies which cameras to export, the time-range to capture, the output format, " +
                       "and optional encryption. Jobs are triggered via Rules using the \"Execute Auto Export Job\" action.",
                Location = new Point(20, 20),
                Size = new Size(560, 100),
                ForeColor = Color.DimGray
            };
            Controls.Add(lbl);
        }
    }
}
