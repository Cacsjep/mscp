using System.Drawing;
using System.Windows.Forms;

namespace AutoExporter.Admin
{
    partial class ExecutionsUserControl
    {
        private System.ComponentModel.IContainer components = null;

        private ToolStrip _toolbar;
        private ToolStripDropDownButton _btnRunNow;
        private ContextMenuStrip _menuRunNow;
        private ToolStripButton _btnRefresh;
        private ToolStripButton _btnClear;

        private DataGridView _grid;
        private Label _lblHint;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Toolbar
            _menuRunNow = new ContextMenuStrip();

            _toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };

            _btnRunNow = new ToolStripDropDownButton("Run Now") { DropDown = _menuRunNow };
            _btnRefresh = new ToolStripButton("Refresh");
            _btnRefresh.Click += OnRefreshClick;

            _btnClear = new ToolStripButton("Clear list");
            _btnClear.Click += OnClearClick;

            _toolbar.Items.Add(_btnRunNow);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_btnRefresh);
            _toolbar.Items.Add(_btnClear);

            // Hint line (status / count). A running export shows as a "Running" row in
            // the grid below rather than a separate progress bar, so there is one place
            // to look and AVI (per-camera) vs XProtect (overall) both read naturally.
            _lblHint = new Label
            {
                Location = new Point(10, 32),
                Size = new Size(700, 18),
                ForeColor = Color.DimGray
            };

            // Grid
            _grid = new DataGridView
            {
                Location = new Point(10, 54),
                Size = new Size(960, 420),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White
            };

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "When", FillWeight = 100 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Job", FillWeight = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Trigger", FillWeight = 50 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Format", FillWeight = 50 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Range", FillWeight = 120 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cameras", FillWeight = 50 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Size", FillWeight = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duration", FillWeight = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Result", FillWeight = 50 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Error", FillWeight = 200 });

            this.Controls.AddRange(new Control[] { _grid, _lblHint, _toolbar });
            this.Size = new Size(980, 540);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
