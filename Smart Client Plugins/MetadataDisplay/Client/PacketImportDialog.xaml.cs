using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Xml.Linq;
using CommunitySDK;

namespace MetadataDisplay.Client
{
    // Editable XML import dialog. Operator pastes a metadata packet, sees it
    // syntax-highlighted live, and on Import we build a LearnSnapshot from
    // the parsed payload. The caller (configuration window / additional-series
    // row) feeds the snapshot into the Topic / Field / Source dropdowns.
    public partial class PacketImportDialog : Window
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        // Set to true while we rebuild the document with highlighted Runs so
        // the TextChanged handler doesn't recurse into itself.
        private bool _rebuilding;

        // Last parse result. Snapshot is exposed via the Snapshot property once
        // the operator clicks Import; null while the input is empty or invalid.
        private LearnSnapshot _lastSnapshot;
        private bool _hasParseableContent;

        // The snapshot produced from the imported XML, available to the caller
        // after ShowDialog() returns true. Topics/keys/source-values mirror the
        // shape produced by Start Learn.
        internal LearnSnapshot Snapshot { get; private set; }

        // The raw XML the user imported. The caller seeds the configuration
        // window's preview re-extract buffer with this so the preview renders
        // the imported packet right away.
        public string ImportedXml { get; private set; }

        public PacketImportDialog()
        {
            InitializeComponent();
            // Style the implicit root Paragraph so the editor doesn't get the
            // default 0,12,0,12 margin (which adds visible top/bottom padding).
            packetRtb.Document = new FlowDocument(new Paragraph { Margin = new Thickness(0) })
            {
                PageWidth = 4000,
            };
            Loaded += (s, e) => packetRtb.Focus();
        }

        // Highlights the current plain-text contents in place while preserving
        // the caret position. WPF doesn't ship a real syntax-highlight editor,
        // so we just clear the Document and rebuild it from colored Runs on
        // every text change. Caret restoration uses TextPointer offsets (not
        // raw character counts) so paragraph breaks don't drift the caret.
        private void OnPacketTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rebuilding) return;

            int caretOffset = packetRtb.Document.ContentStart.GetOffsetToPosition(packetRtb.CaretPosition);
            string text = new TextRange(packetRtb.Document.ContentStart, packetRtb.Document.ContentEnd).Text ?? string.Empty;

            _rebuilding = true;
            try
            {
                var para = new Paragraph { Margin = new Thickness(0) };
                XmlHighlighter.HighlightInto(para, text);
                packetRtb.Document.Blocks.Clear();
                packetRtb.Document.Blocks.Add(para);

                var restored = packetRtb.Document.ContentStart.GetPositionAtOffset(caretOffset)
                               ?? packetRtb.Document.ContentEnd;
                packetRtb.CaretPosition = restored;
            }
            finally { _rebuilding = false; }

            UpdateParseStatus(text);
        }

        // Drives the status line + Import button. We try a full parse via the
        // transient learn session so the operator sees the same Topics/Fields
        // counts they'd see from a real Start Learn capture.
        private void UpdateParseStatus(string xml)
        {
            _lastSnapshot = null;
            _hasParseableContent = false;

            if (string.IsNullOrWhiteSpace(xml))
            {
                statusText.Text = "Paste an XML packet to begin.";
                statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7A, 0x83, 0x88));
                importButton.IsEnabled = false;
                return;
            }

            try
            {
                XDocument.Parse(xml);
            }
            catch (Exception ex)
            {
                statusText.Text = "Invalid XML: " + ex.Message;
                statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD8, 0x39, 0x2C));
                importButton.IsEnabled = false;
                return;
            }

            var snap = LearnSnapshot.FromXml(xml);

            int topicCount = snap.Topics?.Count ?? 0;
            int keyCount = 0;
            int srcCount = 0;
            if (snap.Topics != null)
            {
                foreach (var t in snap.Topics)
                {
                    keyCount += t.DataKeyExamples?.Count ?? 0;
                    if (t.SourceValues != null)
                        foreach (var sv in t.SourceValues) srcCount += sv.Value?.Count ?? 0;
                }
            }

            if (topicCount == 0)
            {
                statusText.Text = "Parsed OK but found no NotificationMessage topics. Paste a metadata packet that contains tt:Message data.";
                statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE6, 0x95, 0x00));
                importButton.IsEnabled = false;
                return;
            }

            _lastSnapshot = snap;
            _hasParseableContent = true;
            statusText.Text = $"Parsed {topicCount} topic(s), {keyCount} field(s), {srcCount} source value(s). Click Import to apply.";
            statusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3C, 0xB3, 0x71));
            importButton.IsEnabled = true;
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            if (!_hasParseableContent || _lastSnapshot == null) return;
            ImportedXml = new TextRange(packetRtb.Document.ContentStart, packetRtb.Document.ContentEnd).Text;
            Snapshot = _lastSnapshot;
            try
            {
                _log.Info($"[ImportPacket] Applied snapshot topics={_lastSnapshot.Topics?.Count ?? 0}");
            }
            catch { }
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Helper for callers: given an applied snapshot, return the topic to
        // auto-select. Picks the first topic in document order (which matches
        // what the operator visually sees as the "top" message in the packet).
        // Returns null when the snapshot has no topics.
        internal static string FirstTopic(LearnSnapshot snap)
        {
            if (snap?.Topics == null) return null;
            foreach (var t in snap.Topics)
            {
                if (!string.IsNullOrEmpty(t?.Topic)) return t.Topic;
            }
            return null;
        }
    }
}
