using System.Drawing;
using System.Windows.Forms;

namespace AutoExporter.Admin
{
    partial class JobUserControl
    {
        private System.ComponentModel.IContainer components = null;

        private TextBox _txtName;
        private CheckBox _chkEnabled;
        private Button _btnDuplicate;

        private GroupBox _grpFormat;
        private RadioButton _radXProtect;
        private RadioButton _radAvi;
        private CheckBox _chkIncludePlayer;
        private CheckBox _chkIncludeAudio;
        private CheckBox _chkEncrypt;
        private Label _lblPassword;
        private TextBox _txtPassword;
        private Label _lblPlaintextWarning;

        private GroupBox _grpRange;
        private Label _lblLast;
        private NumericUpDown _numRangeValue;
        private ComboBox _cboRangeUnit;
        private Label _lblRangeHint;

        private GroupBox _grpTargets;
        private ListBox _lstTargets;
        private Button _btnAddTargets;
        private Button _btnRemoveTarget;

        private GroupBox _grpStorage;
        private Label _lblStoragePath;
        private TextBox _txtStoragePath;
        private Button _btnBrowseStorage;
        private Button _btnVerifyStorage;
        private Label _lblVerifyStatus;
        private Label _lblMaxSize;
        private NumericUpDown _numMaxGB;
        private Label _lblMaxSizeUnit;
        private Label _lblMaxAge;
        private NumericUpDown _numMaxAgeDays;
        private Label _lblMaxAgeUnit;
        private Label _lblStorageHint;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Header
            var lblName = new Label { Text = "Job name:", Location = new Point(12, 14), AutoSize = true };
            _txtName = new TextBox { Location = new Point(90, 11), Size = new Size(360, 23) };
            _txtName.TextChanged += OnUserChange;

            _chkEnabled = new CheckBox { Text = "Enabled", Location = new Point(465, 12), AutoSize = true, Checked = true };
            _chkEnabled.CheckedChanged += OnUserChange;

            _btnDuplicate = new Button { Text = "Duplicate…", Location = new Point(560, 10), Size = new Size(90, 25) };
            _btnDuplicate.Click += OnDuplicateClick;

            // Format group
            _grpFormat = new GroupBox { Text = "Export format", Location = new Point(10, 45), Size = new Size(420, 175) };

            _radXProtect = new RadioButton { Text = "XProtect format (database)", Location = new Point(12, 22), AutoSize = true, Checked = true };
            _radXProtect.CheckedChanged += OnFormatChanged;
            _radAvi = new RadioButton { Text = "AVI", Location = new Point(220, 22), AutoSize = true };
            _radAvi.CheckedChanged += OnFormatChanged;

            _chkIncludePlayer = new CheckBox { Text = "Include Smart Client Player", Location = new Point(12, 50), AutoSize = true, Checked = true };
            _chkIncludePlayer.CheckedChanged += OnUserChange;

            _chkIncludeAudio = new CheckBox { Text = "Include related audio", Location = new Point(12, 75), AutoSize = true, Checked = true };
            _chkIncludeAudio.CheckedChanged += OnUserChange;

            _chkEncrypt = new CheckBox { Text = "Encrypt export (XProtect AES-128)", Location = new Point(12, 102), AutoSize = true };
            _chkEncrypt.CheckedChanged += OnEncryptChanged;

            _lblPassword = new Label { Text = "Password:", Location = new Point(28, 130), AutoSize = true };
            _txtPassword = new TextBox { Location = new Point(95, 127), Size = new Size(220, 23), UseSystemPasswordChar = true, Enabled = false };
            _txtPassword.TextChanged += OnUserChange;

            _lblPlaintextWarning = new Label
            {
                Text = "⚠ Password is stored in plaintext in the management DB.",
                Location = new Point(28, 152),
                Size = new Size(380, 16),
                ForeColor = Color.FromArgb(180, 90, 0),
                Font = new Font(Font, FontStyle.Italic)
            };

            _grpFormat.Controls.AddRange(new Control[]
            {
                _radXProtect, _radAvi, _chkIncludePlayer, _chkIncludeAudio,
                _chkEncrypt, _lblPassword, _txtPassword, _lblPlaintextWarning
            });

            // Range group
            _grpRange = new GroupBox { Text = "Time range", Location = new Point(440, 45), Size = new Size(280, 175) };
            _lblLast = new Label { Text = "Export the last", Location = new Point(12, 30), AutoSize = true };
            _numRangeValue = new NumericUpDown
            {
                Location = new Point(110, 27),
                Size = new Size(60, 23),
                Minimum = 1,
                Maximum = 999,
                Value = 1
            };
            _numRangeValue.ValueChanged += OnUserChange;
            _cboRangeUnit = new ComboBox
            {
                Location = new Point(180, 27),
                Size = new Size(85, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cboRangeUnit.SelectedIndexChanged += OnUserChange;

            _lblRangeHint = new Label
            {
                Text = "Range is anchored at trigger time (manual or rule).\n\nMonths = 30 days.",
                Location = new Point(12, 70),
                Size = new Size(255, 90),
                ForeColor = Color.DimGray
            };
            _grpRange.Controls.AddRange(new Control[] { _lblLast, _numRangeValue, _cboRangeUnit, _lblRangeHint });

            // Targets group
            _grpTargets = new GroupBox { Text = "Cameras / camera groups", Location = new Point(10, 230), Size = new Size(710, 220) };
            _lstTargets = new ListBox
            {
                Location = new Point(12, 22),
                Size = new Size(560, 185),
                SelectionMode = SelectionMode.MultiExtended
            };
            _btnAddTargets = new Button { Text = "Add Camera / Group…", Location = new Point(580, 22), Size = new Size(115, 50) };
            _btnAddTargets.Click += OnAddTargetsClick;
            _btnRemoveTarget = new Button { Text = "Remove", Location = new Point(580, 80), Size = new Size(115, 28) };
            _btnRemoveTarget.Click += OnRemoveTargetClick;

            _grpTargets.Controls.AddRange(new Control[]
            {
                _lstTargets, _btnAddTargets, _btnRemoveTarget
            });

            // Storage & retention group
            _grpStorage = new GroupBox { Text = "Storage && retention", Location = new Point(10, 460), Size = new Size(710, 195) };

            _lblStoragePath = new Label { Text = "Storage path:", Location = new Point(12, 26), AutoSize = true };
            _txtStoragePath = new TextBox { Location = new Point(110, 23), Size = new Size(390, 23) };
            _txtStoragePath.TextChanged += OnUserChange;
            _btnBrowseStorage = new Button { Text = "Browse…", Location = new Point(506, 22), Size = new Size(85, 25) };
            _btnBrowseStorage.Click += OnBrowseStorageClick;
            _btnVerifyStorage = new Button { Text = "Verify on ES", Location = new Point(596, 22), Size = new Size(95, 25) };
            _btnVerifyStorage.Click += OnVerifyStorageClick;

            _lblVerifyStatus = new Label
            {
                Location = new Point(110, 48),
                Size = new Size(580, 18),
                ForeColor = Color.DimGray,
                Font = new Font(Font, FontStyle.Italic)
            };

            _lblMaxSize = new Label { Text = "Max size:", Location = new Point(12, 78), AutoSize = true };
            _numMaxGB = new NumericUpDown
            {
                Location = new Point(110, 75),
                Size = new Size(110, 23),
                Minimum = 0,
                Maximum = 9999999,
                ThousandsSeparator = true
            };
            _numMaxGB.ValueChanged += OnUserChange;
            _lblMaxSizeUnit = new Label { Text = "GB    (0 = unlimited)", Location = new Point(228, 78), AutoSize = true };

            _lblMaxAge = new Label { Text = "Max age:", Location = new Point(12, 108), AutoSize = true };
            _numMaxAgeDays = new NumericUpDown
            {
                Location = new Point(110, 105),
                Size = new Size(110, 23),
                Minimum = 0,
                Maximum = 36500
            };
            _numMaxAgeDays.ValueChanged += OnUserChange;
            _lblMaxAgeUnit = new Label { Text = "days  (0 = unlimited)", Location = new Point(228, 108), AutoSize = true };

            _lblStorageHint = new Label
            {
                Text = "Browse opens THIS machine's filesystem (the picker is local). " +
                       "Verify probes the path from the Event Server with its service account — the only check that matches what export actually sees. " +
                       "Local (D:\\AutoExport\\NightlyLobby) and UNC (\\\\nas\\autoexport\\nightly) both supported.",
                Location = new Point(12, 138),
                Size = new Size(680, 45),
                ForeColor = Color.DimGray
            };

            _grpStorage.Controls.AddRange(new Control[]
            {
                _lblStoragePath, _txtStoragePath, _btnBrowseStorage, _btnVerifyStorage,
                _lblVerifyStatus,
                _lblMaxSize, _numMaxGB, _lblMaxSizeUnit,
                _lblMaxAge, _numMaxAgeDays, _lblMaxAgeUnit,
                _lblStorageHint
            });

            this.Controls.AddRange(new Control[]
            {
                lblName, _txtName, _chkEnabled, _btnDuplicate,
                _grpFormat, _grpRange, _grpTargets, _grpStorage
            });
            this.Size = new Size(740, 670);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
