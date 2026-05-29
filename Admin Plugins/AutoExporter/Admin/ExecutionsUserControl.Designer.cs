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

        private ProgressBar _progressBar;
        private ProgressBar _progressBarCamera;
        private Label _lblProgressText;
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

            // Progress strip (below toolbar): a text line, an overall bar, and a
            // per-camera bar. With many cameras the overall bar barely moves while a
            // single (e.g. AVI) camera exports, so the per-camera bar shows that the
            // current camera is actually progressing.
            _lblProgressText = new Label
            {
                Location = new Point(10, 30),
                Size = new Size(700, 16),
                ForeColor = Color.DimGray
            };
            _progressBar = new ProgressBar
            {
                Location = new Point(10, 48),
                Size = new Size(700, 12),
                Minimum = 0,
                Maximum = 100
            };
            _progressBarCamera = new ProgressBar
            {
                Location = new Point(10, 62),
                Size = new Size(700, 8),
                Minimum = 0,
                Maximum = 100
            };

            // Hint
            _lblHint = new Label
            {
                Location = new Point(10, 76),
                Size = new Size(700, 18),
                ForeColor = Color.DimGray
            };

            // Grid
            _grid = new DataGridView
            {
                Location = new Point(10, 98),
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

            this.Controls.AddRange(new Control[] { _grid, _lblHint, _progressBarCamera, _progressBar, _lblProgressText, _toolbar });
            this.Size = new Size(980, 540);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
