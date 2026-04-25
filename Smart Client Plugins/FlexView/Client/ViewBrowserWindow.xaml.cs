using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using VideoOS.Platform;
using VideoOS.Platform.Client;

namespace FlexView.Client
{
    public enum BrowseMode { SelectFolder, SelectView }

    public partial class ViewBrowserWindow : Window
    {
        private readonly BrowseMode _mode;

        public Item SelectedItem { get; private set; }
        public Item SelectedParent { get; private set; }

        public ViewBrowserWindow(BrowseMode mode)
        {
            _mode = mode;
            InitializeComponent();
            headerText.Text = mode == BrowseMode.SelectFolder
                ? "Select a folder to save the view in"
                : "Select a view to edit";
            searchBox.ToolTip = "Type to filter by name";
            LoadTree();
        }

        private void LoadTree()
        {
            List<Item> groups;
            try
            {
                groups = ClientControl.Instance.GetViewGroupItems();
            }
            catch
            {
                return;
            }

            if (groups == null) return;

            foreach (var group in groups)
            {
                var node = CreateTreeNode(group);
                if (node != null)
                    tree.Items.Add(node);
            }
        }

        private TreeViewItem CreateTreeNode(Item item)
        {
            bool isFolder = item.FQID.FolderType != FolderType.No;

            // In folder mode, skip views (leaves)
            if (_mode == BrowseMode.SelectFolder && !isFolder)
                return null;

            string icon = isFolder ? "\U0001F4C1 " : "\U0001F4CB ";
            var node = new TreeViewItem
            {
                Header = icon + item.Name,
                Tag = item,
                IsExpanded = true,
            };

            if (isFolder)
            {
                var configItem = item as ConfigItem;
                if (configItem != null)
                {
                    var children = configItem.GetChildren();
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            var childNode = CreateTreeNode(child);
                            if (childNode != null)
                                node.Items.Add(childNode);
                        }
                    }
                }
            }

            return node;
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selected = tree.SelectedItem as TreeViewItem;
            if (selected == null)
            {
                btnSelect.IsEnabled = false;
                return;
            }

            var item = selected.Tag as Item;
            if (item == null)
            {
                btnSelect.IsEnabled = false;
                return;
            }

            bool isFolder = item.FQID.FolderType != FolderType.No;

            if (_mode == BrowseMode.SelectFolder)
                btnSelect.IsEnabled = isFolder;
            else
                btnSelect.IsEnabled = !isFolder;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var query = (searchBox.Text ?? string.Empty).Trim();
            ApplyFilter(tree.Items, query);
        }

        // Returns true if the subtree rooted at one of these items has any visible match.
        // A node is visible when it (or any descendant) matches the query; folders auto-expand
        // while a query is active so matches are revealed.
        private bool ApplyFilter(ItemCollection items, string query)
        {
            bool anyVisible = false;
            foreach (var obj in items)
            {
                var node = obj as TreeViewItem;
                if (node == null) continue;

                bool selfMatches = string.IsNullOrEmpty(query) || NodeMatches(node, query);
                bool childMatches = ApplyFilter(node.Items, query);

                bool visible = selfMatches || childMatches;
                node.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

                if (!string.IsNullOrEmpty(query) && childMatches)
                    node.IsExpanded = true;

                if (visible) anyVisible = true;
            }
            return anyVisible;
        }

        private static bool NodeMatches(TreeViewItem node, string query)
        {
            var item = node.Tag as Item;
            if (item == null) return false;
            return (item.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnSelectClick(object sender, RoutedEventArgs e)
        {
            var selected = tree.SelectedItem as TreeViewItem;
            if (selected == null) return;

            SelectedItem = selected.Tag as Item;

            var parentNode = selected.Parent as TreeViewItem;
            if (parentNode != null)
                SelectedParent = parentNode.Tag as Item;

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
