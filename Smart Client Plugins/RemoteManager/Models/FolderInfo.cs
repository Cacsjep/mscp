using System.Collections.Generic;

namespace RemoteManager.Models
{
    /// <summary>
    /// Persisted tag organization data: custom tags assigned to items.
    /// Factory tags (item type, recording server) are generated automatically and not stored here.
    /// </summary>
    public class TagOrganization
    {
        /// <summary>
        /// Maps item keys to lists of custom (user-created) tag names.
        /// Key format: "hw:{guid}" for hardware, "web:{guid}" for user web, "rdp:{guid}" for rdp.
        /// </summary>
        public Dictionary<string, List<string>> ItemTags { get; set; } = new Dictionary<string, List<string>>();
    }
}
