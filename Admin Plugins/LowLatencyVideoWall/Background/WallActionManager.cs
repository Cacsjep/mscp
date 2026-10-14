using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LowLatencyVideoWall.Admin;
using LowLatencyVideoWall.Config;
using LowLatencyVideoWall.Model;
using VideoOS.Platform;
using VideoOS.Platform.Data;
using VideoOS.Platform.RuleAction;

namespace LowLatencyVideoWall.Background
{
    /// <summary>
    /// Everything a rule can do to a wall.
    ///
    ///   Set low latency video wall      switch a user's wall to a stored view
    ///   Clear low latency video wall    show nothing
    ///   Show cameras on ...             replace the wall with a best fit grid of cameras
    ///   Show camera at position ...     put one camera in one numbered pane, keep the rest
    ///
    /// Between them those cover an operator driven wall, a wall composed entirely by rules,
    /// and a wall where a rule only changes one pane.
    ///
    /// Every input is an item picker. ActionDefinition in this SDK offers exactly two of them,
    /// ActionItemKind and ActionItemKind2, and no free text or numeric property at all, which
    /// shapes all four actions:
    ///   - the target operator is picked from the AssignmentKind items, whose item name is the
    ///     Milestone user name the Smart Client reports. That is why a rule can only target a
    ///     user who already has an assignment row: there is nowhere to type a name.
    ///   - a pane position is picked from the generated WallPositionKind items, for the same
    ///     reason: there is nowhere to type a number.
    ///   - "show camera at position" needs three inputs and only has two pickers, so its
    ///     second picker offers the position and the user together. ItemKinds is a collection,
    ///     and the selection comes back as FQIDs that carry their own Kind, so one picker
    ///     holding two kinds costs nothing to read apart.
    /// </summary>
    internal sealed class WallActionManager : ActionManager
    {
        public override Collection<ActionDefinition> GetActionDefinitions()
        {
            try
            {
                // Logged rather than returned, because the rule engine builds the picker from
                // the configuration items itself. A zero here is the usual explanation for an
                // empty dropdown in the Management Client.
                var profiles = WallConfigStore.LoadViewProfiles();
                var assignments = WallConfigStore.LoadAssignments();
                LowLatencyVideoWallDefinition.Log.Info(
                    $"Offering 4 rule actions - {profiles.Count} view profile(s) and " +
                    $"{assignments.Count} assignment(s) available to pick from");
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Could not count view profiles: {ex.Message}", ex);
            }

            return new Collection<ActionDefinition>
            {
                new ActionDefinition
                {
                    Id = LowLatencyVideoWallDefinition.SetWallViewActionId,
                    Name = "Set low latency video wall",
                    SelectionText = "Set low latency video wall <view profile> for <user>",
                    DescriptionText = "Set low latency video wall {0} for {1}",
                    ActionItemKind = new ActionElement
                    {
                        DefaultText = "view profile",
                        ItemKinds = new Collection<Guid>
                        {
                            LowLatencyVideoWallDefinition.ViewProfileKind
                        }
                    },
                    ActionItemKind2 = new ActionElement
                    {
                        DefaultText = "user",
                        ItemKinds = new Collection<Guid>
                        {
                            LowLatencyVideoWallDefinition.AssignmentKind
                        }
                    }
                },

                new ActionDefinition
                {
                    Id = LowLatencyVideoWallDefinition.ClearWallActionId,
                    Name = "Clear low latency video wall",
                    SelectionText = "Clear low latency video wall for <user>",
                    DescriptionText = "Clear low latency video wall for {0}",
                    ActionItemKind = new ActionElement
                    {
                        DefaultText = "user",
                        ItemKinds = new Collection<Guid>
                        {
                            LowLatencyVideoWallDefinition.AssignmentKind
                        }
                    }
                },

                new ActionDefinition
                {
                    Id = LowLatencyVideoWallDefinition.ShowCamerasActionId,
                    Name = "Show cameras on low latency video wall",
                    SelectionText = "Show <cameras> on low latency video wall for <user>",
                    DescriptionText = "Show {0} on low latency video wall for {1}",
                    ActionItemKind = new ActionElement
                    {
                        DefaultText = "cameras",
                        ItemKinds = new Collection<Guid> { Kind.Camera }
                    },
                    ActionItemKind2 = new ActionElement
                    {
                        DefaultText = "user",
                        ItemKinds = new Collection<Guid>
                        {
                            LowLatencyVideoWallDefinition.AssignmentKind
                        }
                    }
                },

                new ActionDefinition
                {
                    Id = LowLatencyVideoWallDefinition.ShowCameraAtActionId,
                    Name = "Show camera at a position on low latency video wall",
                    SelectionText = "Show <camera> at <position and user> on low latency video wall",
                    DescriptionText = "Show {0} at {1} on low latency video wall",
                    ActionItemKind = new ActionElement
                    {
                        DefaultText = "camera",
                        ItemKinds = new Collection<Guid> { Kind.Camera }
                    },

                    // Two kinds in one picker, because this action needs three inputs and the
                    // SDK gives two. Pick the position and the user here.
                    ActionItemKind2 = new ActionElement
                    {
                        DefaultText = "position and user",
                        ItemKinds = new Collection<Guid>
                        {
                            LowLatencyVideoWallDefinition.WallPositionKind,
                            LowLatencyVideoWallDefinition.AssignmentKind
                        }
                    }
                }
            };
        }

        public override void ExecuteAction(Guid actionId, Collection<FQID> actionItems, BaseEvent sourceEvent)
            => Execute(actionId, actionItems, null, sourceEvent);

        /// <summary>
        /// The overload the rule engine uses for a two element action. The base implementation
        /// would drop the second selection, so it has to be overridden.
        /// </summary>
        public override void ExecuteAction2(Guid actionId, Collection<FQID> actionItems,
            Collection<FQID> actionItems2, BaseEvent sourceEvent)
            => Execute(actionId, actionItems, actionItems2, sourceEvent);

        private static void Execute(Guid actionId, Collection<FQID> actionItems,
            Collection<FQID> actionItems2, BaseEvent sourceEvent)
        {
            try
            {
                if (actionId == LowLatencyVideoWallDefinition.ClearWallActionId ||
                    actionId == LowLatencyVideoWallDefinition.ShowCamerasActionId ||
                    actionId == LowLatencyVideoWallDefinition.ShowCameraAtActionId)
                {
                    ExecuteCameraAction(actionId, actionItems, actionItems2, sourceEvent);
                    return;
                }

                if (actionId != LowLatencyVideoWallDefinition.SetWallViewActionId) return;

                var picked = new List<FQID>();
                if (actionItems != null) picked.AddRange(actionItems.Where(f => f != null));
                if (actionItems2 != null) picked.AddRange(actionItems2.Where(f => f != null));

                var trigger = Describe(sourceEvent);

                if (picked.Count == 0)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"'Set low latency video wall' fired by {trigger} with nothing selected in the rule");
                    return;
                }

                // Both selections are classified by the Kind on their FQID rather than by which
                // collection they arrived in, so the action behaves the same whether the rule
                // engine calls ExecuteAction or ExecuteAction2.
                var profileIds = picked
                    .Where(f => f.Kind == LowLatencyVideoWallDefinition.ViewProfileKind)
                    .Select(f => f.ObjectId).Distinct().ToList();

                var assignmentIds = picked
                    .Where(f => f.Kind == LowLatencyVideoWallDefinition.AssignmentKind)
                    .Select(f => f.ObjectId).Distinct().ToList();

                if (profileIds.Count == 0)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"'Set low latency video wall' fired by {trigger} without a view profile selected");
                    return;
                }

                var users = ResolveUsers(assignmentIds);
                if (users.Count == 0)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"'Set low latency video wall' fired by {trigger} without a target user, nothing sent");
                    return;
                }

                var profiles = LoadProfiles();

                foreach (var profileId in profileIds)
                {
                    var profile = profiles.FirstOrDefault(p => p.Id == profileId);
                    if (profile == null)
                    {
                        // The rule still points at it, so the operator has to be told which
                        // rule to repair, not just that something was missing.
                        LowLatencyVideoWallDefinition.Log.Error(
                            $"'Set low latency video wall' fired by {trigger} but view profile {profileId} " +
                            "no longer exists, the rule needs updating");
                        continue;
                    }

                    foreach (var user in users)
                        Send(user, profile, trigger);
                }
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"'Set low latency video wall' failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// The three actions that compose a wall rather than naming a stored view. They share
        /// everything except which payload they build, so they share a method.
        /// </summary>
        private static void ExecuteCameraAction(Guid actionId, Collection<FQID> actionItems,
            Collection<FQID> actionItems2, BaseEvent sourceEvent)
        {
            var picked = new List<FQID>();
            if (actionItems != null) picked.AddRange(actionItems.Where(f => f != null));
            if (actionItems2 != null) picked.AddRange(actionItems2.Where(f => f != null));

            var trigger = Describe(sourceEvent);
            var name = NameOf(actionId);

            // Classified by the Kind on the FQID rather than by which picker it came from, so
            // the mixed position and user picker needs no special handling and the behaviour
            // does not change between ExecuteAction and ExecuteAction2.
            var assignmentIds = picked
                .Where(f => f.Kind == LowLatencyVideoWallDefinition.AssignmentKind)
                .Select(f => f.ObjectId).Distinct().ToList();

            var users = ResolveUsers(assignmentIds);
            if (users.Count == 0)
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"'{name}' fired by {trigger} without a target user, nothing sent");
                return;
            }

            var cameras = picked.Where(f => f.Kind == Kind.Camera).Distinct().ToList();

            var position = picked
                .Where(f => f.Kind == LowLatencyVideoWallDefinition.WallPositionKind)
                .Select(f => WallPositionsItemManager.PositionOf(f.ObjectId))
                .FirstOrDefault(p => p > 0);

            var payload = new WallMessages.SetWallCamerasPayload();

            if (actionId == LowLatencyVideoWallDefinition.ClearWallActionId)
            {
                payload.Command = WallMessages.WallCommand.Clear;
            }
            else if (actionId == LowLatencyVideoWallDefinition.ShowCameraAtActionId)
            {
                if (position <= 0)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"'{name}' fired by {trigger} without a position selected");
                    return;
                }

                if (cameras.Count == 0)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"'{name}' fired by {trigger} without a camera selected");
                    return;
                }

                // One pane holds one camera. Picking several is a mistake worth naming rather
                // than quietly honouring the first.
                if (cameras.Count > 1)
                {
                    LowLatencyVideoWallDefinition.Log.Info(
                        $"'{name}' was given {cameras.Count} cameras for one position, using the first");
                }

                payload.Command = WallMessages.WallCommand.PlaceAt;
                payload.Position = position;
                payload.Cameras.Add(Describe(cameras[0]));
            }
            else
            {
                if (cameras.Count == 0)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"'{name}' fired by {trigger} without a camera selected");
                    return;
                }

                payload.Command = WallMessages.WallCommand.Replace;
                foreach (var camera in cameras) payload.Cameras.Add(Describe(camera));
            }

            foreach (var user in users)
            {
                payload.UserName = user;
                payload.SentUtc = DateTime.UtcNow;

                if (WallEventServerPlugin.Broadcast(WallMessages.SetWallCameras, payload.ToXml()))
                {
                    LowLatencyVideoWallDefinition.Log.Info(
                        $"Sent '{payload.Describe()}' to user '{user}', triggered by {trigger}");
                }
                else
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"'{payload.Describe()}' for user '{user}' was not sent");
                }
            }
        }

        /// <summary>
        /// A camera FQID with the name the rule author saw. Resolved here, on the Event Server,
        /// so a client that cannot read the configuration still has something to put in the
        /// pane title.
        /// </summary>
        private static WallMessages.WallCameraRef Describe(FQID camera)
        {
            var name = "";
            try
            {
                name = Configuration.Instance.GetItem(camera)?.Name ?? "";
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Info(
                    $"Could not read the name of camera {camera.ObjectId}: {ex.Message}");
            }

            return new WallMessages.WallCameraRef { Id = camera.ObjectId, Name = name };
        }

        private static string NameOf(Guid actionId)
        {
            if (actionId == LowLatencyVideoWallDefinition.ClearWallActionId)
                return "Clear low latency video wall";
            if (actionId == LowLatencyVideoWallDefinition.ShowCamerasActionId)
                return "Show cameras on low latency video wall";
            if (actionId == LowLatencyVideoWallDefinition.ShowCameraAtActionId)
                return "Show camera at a position on low latency video wall";
            return "Set low latency video wall";
        }

        private static void Send(string userName, ViewProfile profile, string trigger)
        {
            try
            {
                var payload = new WallMessages.SetWallViewPayload
                {
                    UserName = userName,
                    ViewProfileId = profile.Id,
                    ViewProfileName = profile.Name ?? "",
                    SentUtc = DateTime.UtcNow
                };

                if (WallEventServerPlugin.Broadcast(WallMessages.SetWallView, payload.ToXml()))
                {
                    LowLatencyVideoWallDefinition.Log.Info(
                        $"Sent wall switch to user '{userName}': view '{profile.Name}' ({profile.Id}), " +
                        $"{profile.Panes.Count} pane(s), triggered by {trigger}");
                }
                else
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"Wall switch to user '{userName}' for view '{profile.Name}' was not sent");
                }
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error(
                    $"Sending wall switch to user '{userName}' failed: {ex.Message}", ex);
            }
        }

        private static List<ViewProfile> LoadProfiles()
        {
            try
            {
                return WallConfigStore.LoadViewProfiles();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Reading view profiles failed: {ex.Message}", ex);
                return new List<ViewProfile>();
            }
        }

        /// <summary>
        /// Turns the picked assignment items into the user names they carry. The assignment is
        /// only used as the place a user name is stored, its own view profile is irrelevant
        /// here, because the rule says which view to switch to.
        /// </summary>
        private static List<string> ResolveUsers(List<Guid> assignmentIds)
        {
            var users = new List<string>();
            if (assignmentIds == null || assignmentIds.Count == 0) return users;

            List<WallAssignment> assignments;
            try
            {
                assignments = WallConfigStore.LoadAssignments();
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Reading wall assignments failed: {ex.Message}", ex);
                return users;
            }

            foreach (var id in assignmentIds)
            {
                var assignment = assignments.FirstOrDefault(a => a.Id == id);
                if (assignment == null)
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"Rule points at wall assignment {id}, which no longer exists");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(assignment.UserName))
                {
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"Wall assignment {id} has no user name, cannot target a client");
                    continue;
                }

                if (!users.Any(u => string.Equals(u, assignment.UserName, StringComparison.OrdinalIgnoreCase)))
                    users.Add(assignment.UserName);
            }

            return users;
        }

        private static string Describe(BaseEvent sourceEvent)
        {
            try
            {
                var header = sourceEvent?.EventHeader;
                if (header == null) return "an unnamed event";

                var name = !string.IsNullOrWhiteSpace(header.Message) ? header.Message : header.Name;
                return string.IsNullOrWhiteSpace(name) ? $"event type '{header.Type}'" : $"'{name}'";
            }
            catch
            {
                return "an unnamed event";
            }
        }
    }
}
