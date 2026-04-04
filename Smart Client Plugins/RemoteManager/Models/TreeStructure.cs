using System;
using System.Collections.Generic;

namespace RemoteManager.Models
{
    /// <summary>
    /// Persisted tree structure data. Only tracks hierarchy (which items are in which folders).
    /// Actual item data is stored separately in pipe-delimited format.
    /// </summary>
    public class TreeStructure
    {
        public List<TreeNodeData> Children { get; set; } = new List<TreeNodeData>();
    }

    public class TreeNodeData
    {
        /// <summary>
        /// "folder", "hardware", "website", "rdp"
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// GUID id of the item (or folder).
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Display name (only used for folders).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Whether the folder is expanded in the tree.
        /// </summary>
        public bool Expanded { get; set; }

        /// <summary>
        /// Child nodes (only for folders).
        /// </summary>
        public List<TreeNodeData> Children { get; set; }
    }
}
