using System;
using System.Collections.Generic;
using System.Linq;
using LowLatencyVideoWall.Model;
using VideoOS.Platform;

namespace LowLatencyVideoWall.Config
{
    /// <summary>
    /// Reads and writes everything the plugin owns through the Milestone configuration, so
    /// view profiles and assignments live on the management server and every Smart Client
    /// and the Event Server see the same thing without a separate database.
    ///
    /// Each profile and assignment is one configuration item whose payload is the XML the
    /// model serialises to. Keeping the payload in one property rather than spreading it
    /// over many keeps versioning simple.
    /// </summary>
    internal static class WallConfigStore
    {
        private const string PayloadKey = "Payload";

        // ---- view profiles --------------------------------------------------------------

        internal static List<ViewProfile> LoadViewProfiles()
        {
            var result = new List<ViewProfile>();

            foreach (var item in GetItems(LowLatencyVideoWallDefinition.ViewProfileKind))
            {
                var profile = ViewProfile.FromXml(item.Properties.ContainsKey(PayloadKey)
                    ? item.Properties[PayloadKey]
                    : null);

                if (profile == null) continue;
                result.Add(profile);
            }

            return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        internal static ViewProfile LoadViewProfile(Guid id)
            => LoadViewProfiles().FirstOrDefault(p => p.Id == id);

        internal static void SaveViewProfile(ViewProfile profile)
        {
            if (profile == null) return;
            SaveItem(LowLatencyVideoWallDefinition.ViewProfileKind, profile.Id,
                profile.Name, profile.ToXml());
        }

        internal static void DeleteViewProfile(Guid id)
            => DeleteItem(LowLatencyVideoWallDefinition.ViewProfileKind, id);

        // ---- assignments ----------------------------------------------------------------

        internal static List<WallAssignment> LoadAssignments()
        {
            var result = new List<WallAssignment>();

            foreach (var item in GetItems(LowLatencyVideoWallDefinition.AssignmentKind))
            {
                var assignment = WallAssignment.FromXml(item.Properties.ContainsKey(PayloadKey)
                    ? item.Properties[PayloadKey]
                    : null);

                if (assignment == null) continue;
                result.Add(assignment);
            }

            return result.OrderBy(a => a.UserName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// The view a user should see at login. User names are matched case insensitively and
        /// a domain prefix is ignored, because the Smart Client reports the logged in user in
        /// a different form depending on how the site authenticates.
        /// </summary>
        internal static ViewProfile ResolveForUser(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return null;

            var assignment = LoadAssignments().FirstOrDefault(a => UserMatches(a.UserName, userName));
            return assignment == null ? null : LoadViewProfile(assignment.ViewProfileId);
        }

        internal static bool UserMatches(string configured, string actual)
        {
            if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(actual))
                return false;

            return string.Equals(StripDomain(configured), StripDomain(actual),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string StripDomain(string user)
        {
            var s = (user ?? "").Trim();
            var slash = s.LastIndexOf('\\');
            if (slash >= 0 && slash < s.Length - 1) s = s.Substring(slash + 1);
            var at = s.IndexOf('@');
            if (at > 0) s = s.Substring(0, at);
            return s;
        }

        internal static void SaveAssignment(WallAssignment assignment)
        {
            if (assignment == null) return;
            SaveItem(LowLatencyVideoWallDefinition.AssignmentKind, assignment.Id,
                assignment.UserName, assignment.ToXml());
        }

        internal static void DeleteAssignment(Guid id)
            => DeleteItem(LowLatencyVideoWallDefinition.AssignmentKind, id);

        // ---- plumbing -------------------------------------------------------------------

        private static List<Item> GetItems(Guid kind)
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    LowLatencyVideoWallDefinition.PluginId, null, kind);

                return items?.ToList() ?? new List<Item>();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Reading configuration kind {kind} failed: {ex.Message}");
                return new List<Item>();
            }
        }

        private static void SaveItem(Guid kind, Guid id, string name, string payload)
        {
            try
            {
                // ParentId is Guid.Empty, not the plugin id. These are root level items of a
                // root level ItemNode, and that is the shape the Management Client hands out
                // in suggestedFQID. Naming the plugin as the parent instead invents a parent
                // item that does not exist, which is enough to keep the items out of the
                // pickers that build a tree rather than a flat list.
                var fqid = new FQID
                {
                    ServerId = EnvironmentManager.Instance.MasterSite.ServerId,
                    ParentId = Guid.Empty,
                    ObjectId = id,
                    Kind = kind,
                    FolderType = FolderType.No
                };

                var item = new Item(fqid, string.IsNullOrWhiteSpace(name) ? id.ToString() : name);
                item.Properties[PayloadKey] = payload ?? "";

                Configuration.Instance.SaveItemConfiguration(
                    LowLatencyVideoWallDefinition.PluginId, item);
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Saving configuration item {id} failed: {ex.Message}");
                throw;
            }
        }

        private static void DeleteItem(Guid kind, Guid id)
        {
            try
            {
                var item = GetItems(kind).FirstOrDefault(i => i.FQID?.ObjectId == id);
                if (item == null) return;

                Configuration.Instance.DeleteItemConfiguration(
                    LowLatencyVideoWallDefinition.PluginId, item);
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Deleting configuration item {id} failed: {ex.Message}");
                throw;
            }
        }
    }
}
