using System;
using System.Collections.ObjectModel;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Data;
using VideoOS.Platform.RuleAction;

namespace AutoExporter.Background
{
    public class AutoExporterActionManager : ActionManager
    {
        private static readonly PluginLog _log = new PluginLog("AutoExporter.ActionManager");

        // Stable action id so rule references survive plugin restarts.
        internal static readonly Guid ExecuteJobActionId = new Guid("69263570-70DD-4B41-BE1D-F040218F95C0");

        public override Collection<ActionDefinition> GetActionDefinitions()
        {
            return new Collection<ActionDefinition>
            {
                new ActionDefinition
                {
                    Id = ExecuteJobActionId,
                    Name = "Execute Auto Export Job",
                    SelectionText = "Execute <Auto Export Job>",
                    DescriptionText = "Execute {0}",
                    ActionItemKind = new ActionElement
                    {
                        DefaultText = "Auto Export Job",
                        ItemKinds = new Collection<Guid> { AutoExporterDefinition.JobKindId }
                    }
                }
            };
        }

        public override void ExecuteAction(Guid actionId, Collection<FQID> actionItems, BaseEvent sourceEvent)
        {
            if (actionId != ExecuteJobActionId) return;

            var bg = AutoExporterBackgroundPlugin.Instance;
            if (bg == null)
            {
                _log.Error("Background plugin not available for action execution");
                return;
            }

            foreach (var fqid in actionItems)
            {
                try
                {
                    _log.Info($"Rule action triggered: targetKind={fqid.Kind}, targetId={fqid.ObjectId}");
                    bg.TriggerJob(fqid.ObjectId, sourceEvent, "Rule");
                }
                catch (Exception ex)
                {
                    _log.Error($"Error executing action for {fqid.ObjectId}: {ex.Message}", ex);
                }
            }
        }
    }
}
