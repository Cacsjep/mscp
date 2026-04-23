using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using VideoOS.Platform;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace BarcodeReader.Admin
{
    public partial class QRCodeConfigUserControl : UserControl
    {
        // Preview QR is regenerated on every Payload keystroke - render is cheap (~1 ms
        // for a 256 px code) but we still debounce to avoid flicker during fast typing.
        private const int PreviewSizePx = 256;

        internal event EventHandler ConfigurationChangedByUser;

        private bool _suspendChange;
        private readonly Timer _regenDebounce;

        public QRCodeConfigUserControl()
        {
            InitializeComponent();

            // Create the debounce timer BEFORE the combo-box is populated. Setting
            // _cboEC.SelectedIndex synchronously fires SelectedIndexChanged which lands in
            // OnUserChange and touches _regenDebounce; populating the list before the
            // timer exists was the cause of a NullReferenceException on panel open.
            _regenDebounce = new Timer { Interval = 180 };
            _regenDebounce.Tick += (s, e) => { _regenDebounce.Stop(); RegeneratePreview(); };

            _suspendChange = true;
            try
            {
                _cboEC.Items.Clear();
                _cboEC.Items.AddRange(new object[] {
                    "L  Low (~7% recoverable)",
                    "M  Medium (~15%, default)",
                    "Q  Quartile (~25%)",
                    "H  High (~30%)"
                });
                _cboEC.SelectedIndex = 1;
            }
            finally { _suspendChange = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _regenDebounce?.Stop();
                _regenDebounce?.Dispose();
                var old = _pbPreview?.Image;
                if (_pbPreview != null) _pbPreview.Image = null;
                old?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        public string DisplayName => _txtName.Text;

        public string GetPayload() => _txtPayload.Text ?? "";

        public void FillContent(Item item)
        {
            _suspendChange = true;
            try
            {
                _txtName.Text    = item?.Name ?? "";
                _txtPayload.Text = item != null && item.Properties.ContainsKey(QRCodeConfig.KeyPayload)
                    ? item.Properties[QRCodeConfig.KeyPayload] : "";

                var ec = item != null && item.Properties.ContainsKey(QRCodeConfig.KeyErrorCorrection)
                    ? item.Properties[QRCodeConfig.KeyErrorCorrection] : QRCodeConfig.DefaultErrorCorrection;
                SelectEC(ec);
            }
            finally { _suspendChange = false; }

            RegeneratePreview();
        }

        public void ClearContent()
        {
            _suspendChange = true;
            try
            {
                _txtName.Text = "";
                _txtPayload.Text = "";
                _cboEC.SelectedIndex = 1;
                var old = _pbPreview.Image;
                _pbPreview.Image = null;
                old?.Dispose();
            }
            finally { _suspendChange = false; }
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text)) return "Please enter a name.";
            if (string.IsNullOrEmpty(_txtPayload.Text)) return "Payload cannot be empty.";
            // Max payload length for QR with H error correction at highest version is ~2953
            // bytes. Anything longer than that can't be represented  block it up front.
            if (_txtPayload.Text.Length > 2900)
                return "Payload is too long to fit in a QR code (max ~2900 chars).";
            return null;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;
            item.Name = _txtName.Text;
            item.Properties[QRCodeConfig.KeyPayload] = _txtPayload.Text ?? "";
            item.Properties[QRCodeConfig.KeyErrorCorrection] = GetSelectedEC();
        }

        #region Preview

        private void SelectEC(string code)
        {
            var idx = 1;
            switch ((code ?? "").ToUpperInvariant())
            {
                case "L": idx = 0; break;
                case "M": idx = 1; break;
                case "Q": idx = 2; break;
                case "H": idx = 3; break;
            }
            _cboEC.SelectedIndex = idx;
        }

        private string GetSelectedEC()
        {
            switch (_cboEC.SelectedIndex)
            {
                case 0: return "L";
                case 2: return "Q";
                case 3: return "H";
                default: return "M";
            }
        }

        private ErrorCorrectionLevel GetSelectedECLevel()
        {
            switch (GetSelectedEC())
            {
                case "L": return ErrorCorrectionLevel.L;
                case "Q": return ErrorCorrectionLevel.Q;
                case "H": return ErrorCorrectionLevel.H;
                default:  return ErrorCorrectionLevel.M;
            }
        }

        private void RegeneratePreview()
        {
            try
            {
                var payload = _txtPayload.Text ?? "";
                if (payload.Length == 0)
                {
                    var old0 = _pbPreview.Image;
                    _pbPreview.Image = null;
                    old0?.Dispose();
                    return;
                }

                var writer = new BarcodeWriter
                {
                    Format = BarcodeFormat.QR_CODE,
                    Options = new QrCodeEncodingOptions
                    {
                        Width = PreviewSizePx,
                        Height = PreviewSizePx,
                        Margin = 1,
                        ErrorCorrection = GetSelectedECLevel()
                    }
                };
                var bmp = writer.Write(payload);
                var old = _pbPreview.Image;
                _pbPreview.Image = bmp;
                old?.Dispose();
            }
            catch
            {
                // Payload that can't be encoded (e.g. exceeds capacity at the chosen EC
                // level) - just clear the preview. ValidateInput will stop the save.
                var old = _pbPreview.Image;
                _pbPreview.Image = null;
                old?.Dispose();
            }
        }

        #endregion

        #region Event handlers

        internal void OnUserChange(object sender, EventArgs e)
        {
            if (_suspendChange) return;

            // Debounce preview regeneration when Payload or EC changes; name/notes
            // don't affect the encoded image so skip the regen there. Null-check on the
            // timer is defensive: any constructor-time setter that synchronously fires
            // SelectedIndexChanged / TextChanged could land here before the field is set
            // (that was the cause of an earlier NRE).
            if (_regenDebounce != null && (sender == _txtPayload || sender == _cboEC))
            {
                _regenDebounce.Stop();
                _regenDebounce.Start();
            }

            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void BtnCopyPng_Click(object sender, EventArgs e)
        {
            var img = _pbPreview.Image;
            if (img == null) { SystemSounds.Beep(); return; }
            try { Clipboard.SetImage(img); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not copy to clipboard: " + ex.Message,
                    "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnSavePng_Click(object sender, EventArgs e)
        {
            var img = _pbPreview.Image;
            if (img == null) { SystemSounds.Beep(); return; }

            using (var sfd = new SaveFileDialog
            {
                Filter = "PNG Image|*.png",
                FileName = Sanitize(_txtName.Text) + ".png",
                OverwritePrompt = true,
            })
            {
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    // Re-render at 512 px for export so file isn't pixelated even though
                    // the in-form preview is 256 px. Cheap - ZXing encode is ~1 ms.
                    var writer = new BarcodeWriter
                    {
                        Format = BarcodeFormat.QR_CODE,
                        Options = new QrCodeEncodingOptions
                        {
                            Width = 512,
                            Height = 512,
                            Margin = 1,
                            ErrorCorrection = GetSelectedECLevel()
                        }
                    };
                    using (var bmp = writer.Write(_txtPayload.Text ?? ""))
                    {
                        bmp.Save(sfd.FileName, ImageFormat.Png);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Save failed: " + ex.Message,
                        "Save PNG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "qrcode";
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            return new string(chars);
        }

        #endregion

        private static class SystemSounds
        {
            public static void Beep() => System.Media.SystemSounds.Beep.Play();
        }
    }
}
