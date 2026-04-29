using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ColoredTimeline.Admin
{
    // Modal picker for an EventType.Name. Tree is grouped by ConfigAPI EventTypeGroup
    // ("Device - Predefined" / "Device - Configurable") and, for ONVIF-style names
    // (tns1:.../... and tnsaxis:.../...), by the slash-separated path so the 329
    // Configurable events become a navigable tree of ONVIF topics.
    //
    //  - "tns1:" / "tnsaxis:" prefixes are stripped from display (Tag still holds
    //    the original full name, which is what the event log filters on).
    //  - Path segments become nested tree nodes; the last segment is the leaf.
    //  - Names without ":" (and without "/") stay flat under their group.
    //  - Live filter on the search box rebuilds the tree to only show matches.
    internal class EventPickerDialog : Form
    {
        private readonly List<EventTypeCache.Entry> _items;
        private TextBox _txtFilter;
        private TreeView _tree;
        private Button _btnOk;
        private Button _btnCancel;
        private Label _lblHint;

        public string SelectedEvent { get; private set; }

        public EventPickerDialog(IEnumerable<EventTypeCache.Entry> items, string initial)
        {
            _items = items?.ToList() ?? new List<EventTypeCache.Entry>();
            SelectedEvent = initial;

            InitializeComponent();
            RebuildTree("");
            SelectInitial();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "Pick event";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 600);
            ClientSize = new Size(720, 720);
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;
            KeyPreview = true;

            var lblFilter = new Label
            {
                Text = "Search:",
                Location = new Point(12, 16),
                AutoSize = true
            };
            _txtFilter = new TextBox
            {
                Location = new Point(70, 13),
                Size = new Size(630, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _txtFilter.TextChanged += (s, e) => RebuildTree(_txtFilter.Text);
            _txtFilter.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down) { _tree.Focus(); e.Handled = true; }
            };

            _tree = new TreeView
            {
                Location = new Point(12, 46),
                Size = new Size(696, 620),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                HideSelection = false,
                ShowLines = true,
                ShowRootLines = true
            };
            _tree.AfterSelect += (s, e) => _btnOk.Enabled = IsLeaf(e.Node);
            _tree.NodeMouseDoubleClick += (s, e) =>
            {
                if (IsLeaf(e.Node)) { CommitSelection(); }
            };

            _lblHint = new Label
            {
                Location = new Point(12, 678),
                AutoSize = true,
                ForeColor = SystemColors.ControlDarkDark,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Text = ""
            };

            _btnOk = new Button
            {
                Text = "OK",
                Location = new Point(540, 684),
                Size = new Size(80, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = false,
                DialogResult = DialogResult.None
            };
            _btnOk.Click += (s, e) => CommitSelection();

            _btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(626, 684),
                Size = new Size(80, 26),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Controls.AddRange(new Control[] { lblFilter, _txtFilter, _tree, _lblHint, _btnOk, _btnCancel });
            ResumeLayout(false);
            PerformLayout();
        }

        private static bool IsLeaf(TreeNode n) => n != null && n.Nodes.Count == 0 && n.Tag is string;

        private void CommitSelection()
        {
            if (_tree.SelectedNode?.Tag is string s)
            {
                SelectedEvent = s;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        // Each tree node maps to one path segment; leaves carry the original full
        // event name in Tag (so event-log filtering still uses the unstripped name).
        private class Node
        {
            public string Label;
            public SortedDictionary<string, Node> Children =
                new SortedDictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            public string FullName; // non-null on leaves
            public int LeafCount;
        }

        private void RebuildTree(string filter)
        {
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                var f = (filter ?? "").Trim();

                // Top-level: by EventTypeGroup (Predefined / Configurable).
                var roots = new SortedDictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
                int total = 0;

                foreach (var item in _items)
                {
                    if (!string.IsNullOrEmpty(f) && item.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (!roots.TryGetValue(item.Group, out var rootNode))
                    {
                        rootNode = new Node { Label = item.Group };
                        roots[item.Group] = rootNode;
                    }
                    rootNode.LeafCount++;
                    total++;

                    Insert(rootNode, item.Name);
                }

                foreach (var rootNode in roots.Values)
                {
                    var topUi = new TreeNode(rootNode.Label);
                    AppendChildren(topUi, rootNode);
                    _tree.Nodes.Add(topUi);
                }

                if (!string.IsNullOrEmpty(f))
                    _tree.ExpandAll();
                else
                    foreach (TreeNode n in _tree.Nodes) n.Expand();

                _lblHint.Text = $"{total} event(s)";
            }
            finally
            {
                _tree.EndUpdate();
            }
        }

        // Strip namespace prefix and split path. Names without ":" and without "/"
        // remain a single-segment leaf under their group.
        private static string[] BuildPath(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return new[] { "(empty)" };

            string bare = fullName;
            if (bare.StartsWith("tns1:", StringComparison.OrdinalIgnoreCase))
                bare = bare.Substring("tns1:".Length);
            else if (bare.StartsWith("tnsaxis:", StringComparison.OrdinalIgnoreCase))
                bare = bare.Substring("tnsaxis:".Length);
            else
            {
                int colon = bare.IndexOf(':');
                if (colon > 0) bare = bare.Substring(colon + 1);
            }

            var parts = bare.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts : new[] { bare };
        }

        private static void Insert(Node root, string fullName)
        {
            var parts = BuildPath(fullName);
            var current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!current.Children.TryGetValue(parts[i], out var child))
                {
                    child = new Node { Label = parts[i] };
                    current.Children[parts[i]] = child;
                }
                current = child;
            }

            // ONVIF boolean-state suffix: trailing "-0"/"/0" = Falling, "-1"/"/1" = Rising.
            // Milestone renders it as "Name Falling/Rising" in Mgmt Client; mirror that.
            var leafRaw = parts[parts.Length - 1];
            var leafLabel = leafRaw;
            if (leafRaw == "0") leafLabel = "Falling";
            else if (leafRaw == "1") leafLabel = "Rising";
            else if (leafRaw.EndsWith("-0", StringComparison.Ordinal))
                leafLabel = leafRaw.Substring(0, leafRaw.Length - 2) + " (Falling)";
            else if (leafRaw.EndsWith("-1", StringComparison.Ordinal))
                leafLabel = leafRaw.Substring(0, leafRaw.Length - 2) + " (Rising)";

            // Disambiguate identical leaf labels under the same parent (rare, but
            // possible if two different prefixes produced the same suffix).
            var key = leafLabel;
            int dupe = 1;
            while (current.Children.ContainsKey(key))
                key = leafLabel + " #" + (++dupe);

            current.Children[key] = new Node { Label = leafLabel, FullName = fullName };
        }

        private static void AppendChildren(TreeNode uiParent, Node modelParent)
        {
            // Branches first, then leaves alphabetically.
            foreach (var kv in modelParent.Children.Where(c => c.Value.FullName == null))
            {
                var branch = kv.Value;
                var ui = new TreeNode(branch.Label);
                AppendChildren(ui, branch);
                uiParent.Nodes.Add(ui);
            }
            foreach (var kv in modelParent.Children.Where(c => c.Value.FullName != null))
            {
                var leaf = kv.Value;
                uiParent.Nodes.Add(new TreeNode(leaf.Label) { Tag = leaf.FullName });
            }
        }

        private void SelectInitial()
        {
            if (string.IsNullOrEmpty(SelectedEvent)) return;
            var match = FindLeaf(_tree.Nodes, SelectedEvent);
            if (match != null)
            {
                _tree.SelectedNode = match;
                match.EnsureVisible();
            }
        }

        private static TreeNode FindLeaf(TreeNodeCollection nodes, string text)
        {
            foreach (TreeNode n in nodes)
            {
                if (IsLeaf(n) && string.Equals(n.Tag as string, text, StringComparison.OrdinalIgnoreCase))
                    return n;
                var rec = FindLeaf(n.Nodes, text);
                if (rec != null) return rec;
            }
            return null;
        }
    }
}
