using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RemoteManager.Models
{
    public enum RemoteNodeType
    {
        Root,
        Folder,
        HardwareWebsite,
        UserWebsite,
        RdpConnection
    }

    public class RemoteTreeNode : INotifyPropertyChanged
    {
        private string _name;
        private bool _isExpanded;
        private bool _isVisible = true;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public RemoteNodeType NodeType { get; }

        /// <summary>
        /// ID referencing the actual data entry (HardwareId for hw/web, Id for RDP, generated for folders).
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Whether this node is system-defined (root, hardware devices).
        /// System nodes cannot be deleted but can be moved.
        /// </summary>
        public bool IsSystemDefined { get; }

        /// <summary>
        /// Whether this node's properties can be edited (user websites, RDP connections).
        /// </summary>
        public bool IsEditable =>
            NodeType == RemoteNodeType.UserWebsite ||
            NodeType == RemoteNodeType.RdpConnection;

        /// <summary>
        /// Whether this node can be renamed (user folders only).
        /// </summary>
        public bool IsRenamable => NodeType == RemoteNodeType.Folder && !IsSystemDefined;

        public RemoteTreeNode Parent { get; internal set; }
        public ObservableCollection<RemoteTreeNode> Children { get; }

        public RemoteTreeNode(string name, RemoteNodeType nodeType, Guid id,
            bool isSystemDefined = false, bool isExpanded = false)
        {
            _name = name;
            NodeType = nodeType;
            Id = id;
            IsSystemDefined = isSystemDefined;
            _isExpanded = isExpanded;
            Children = new ObservableCollection<RemoteTreeNode>();
            Children.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (RemoteTreeNode child in e.NewItems)
                        child.Parent = this;
                if (e.OldItems != null)
                    foreach (RemoteTreeNode child in e.OldItems)
                        child.Parent = null;
            };
        }

        public bool IsAncestorOf(RemoteTreeNode node)
        {
            var current = node.Parent;
            while (current != null)
            {
                if (current == this) return true;
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// Whether this node can be deleted.
        /// System nodes cannot be deleted.
        /// Folders can be deleted (system children get moved to root).
        /// </summary>
        public bool CanDelete()
        {
            if (IsSystemDefined) return false;
            if (NodeType == RemoteNodeType.Root) return false;
            return true;
        }

        public void MoveTo(RemoteTreeNode newParent)
        {
            if (newParent == this || newParent == Parent) return;
            if (IsAncestorOf(newParent)) return;
            Parent?.Children.Remove(this);
            newParent.Children.Add(this);
        }

        /// <summary>
        /// Removes this node from its parent. If this is a folder containing
        /// system nodes, those are moved to the specified fallback parent first.
        /// </summary>
        public void Delete(RemoteTreeNode systemNodeFallback)
        {
            if (NodeType == RemoteNodeType.Folder)
                RescueSystemChildren(this, systemNodeFallback);
            Parent?.Children.Remove(this);
        }

        private static void RescueSystemChildren(RemoteTreeNode folder, RemoteTreeNode fallback)
        {
            for (int i = folder.Children.Count - 1; i >= 0; i--)
            {
                var child = folder.Children[i];
                if (child.NodeType == RemoteNodeType.Folder)
                    RescueSystemChildren(child, fallback);

                if (child.IsSystemDefined)
                {
                    folder.Children.RemoveAt(i);
                    fallback.Children.Add(child);
                }
            }
        }

        /// <summary>
        /// Whether this node is a container (can hold children).
        /// </summary>
        public bool IsContainer =>
            NodeType == RemoteNodeType.Root || NodeType == RemoteNodeType.Folder;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
