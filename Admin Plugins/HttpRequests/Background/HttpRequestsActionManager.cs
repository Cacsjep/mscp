using System;
using System.Collections.ObjectModel;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Data;
using VideoOS.Platform.RuleAction;

namespace HttpRequests.Background
{
    public class HttpRequestsActionManager : ActionManager
    {
        private static readonly PluginLog _log = new PluginLog("HttpRequests.ActionManager");

        internal static readonly Guid ExecuteRequestActionId = new Guid("C4A1B2D3-E5F6-4789-AB01-23456789A060");

        public override Collection<ActionDefinition> GetActionDefinitions()
        {
            return new Collection<ActionDefinition>
            {
                new ActionDefinition
                {
                    Id = ExecuteRequestActionId,
                    Name = "Execute HTTP Request",
                    SelectionText = "Execute <HTTP Request>",
                    DescriptionText = "Execute {0}",
                    ActionItemKind = new ActionElement
                    {
                        DefaultText = "HTTP Request",
                        ItemKinds = new Collection<Guid> { HttpRequestsDefinition.RequestKindId }
                    }
                }
            };
        }

        public override void ExecuteAction(Guid actionId, Collection<FQID> actionItems, BaseEvent sourceEvent)
        {
            if (actionId != ExecuteRequestActionId)
                return;

            var bgPlugin = HttpRequestsBackgroundPlugin.Instance;
            if (bgPlugin == null)
            {
                _log.Error("Background plugin not available for action execution");
                return;
            }

            _log.Info($"sourceEvent type: {sourceEvent?.GetType().FullName ?? "null"}, hasHeader: {(sourceEvent?.EventHeader != null)}");

            foreach (var fqid in actionItems)
            {
                try
                {
                    _log.Info($"Rule action triggered: targetKind={fqid.Kind}, targetId={fqid.ObjectId}");
                    bgPlugin.HandleAction(fqid, sourceEvent);
                }
                catch (Exception ex)
                {
                    _log.Error($"Error executing action for {fqid.ObjectId}: {ex.Message}", ex);
                }
            }
        }
    }
}
