using System.Drawing;
using System.Windows.Forms;

namespace AutoExporter.Admin
{
    partial class StatusUserControl
    {
        private System.ComponentModel.IContainer components = null;

        private ToolStrip _toolbar;
        private ToolStripButton _btnRefresh;
        private Label _lblLastRefresh;
        private Label _lblHint;
        private DataGridView _grid;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            _toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            _btnRefresh = new ToolStripButton("Refresh");
            _toolbar.Items.Add(_btnRefresh);

            _lblLastRefresh = new Label
            {
                Location = new Point(10, 32),
                Size = new Size(500, 18),
                ForeColor = Color.DimGray
            };

            _lblHint = new Label
            {
                Location = new Point(10, 52),
                Size = new Size(700, 18),
                ForeColor = Color.DimGray
            };

            _grid = new DataGridView
            {
                Location = new Point(10, 76),
                Size = new Size(960, 460),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White
            };

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Job", FillWeight = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Storage path", FillWeight = 200 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", FillWeight = 80 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Detail", FillWeight = 150 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Usage", FillWeight = 70 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quota", FillWeight = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Disk free / total", FillWeight = 110 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Runs", FillWeight = 45 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Age cap", FillWeight = 55 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Oldest run", FillWeight = 100 });

            this.Controls.AddRange(new Control[] { _grid, _lblHint, _lblLastRefresh, _toolbar });
            this.Size = new Size(980, 560);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
