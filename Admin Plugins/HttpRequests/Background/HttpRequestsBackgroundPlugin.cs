using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CommunitySDK;
using HttpRequests.Messaging;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Data;
using VideoOS.Platform.Messaging;

namespace HttpRequests.Background
{
    public class HttpRequestsBackgroundPlugin : BackgroundPlugin
    {
        private static readonly PluginLog _log = new PluginLog("HttpRequests");
        private readonly SystemLog _sysLog = new SystemLog(_log);
        private readonly CrossMessageHandler _cmh = new CrossMessageHandler(_log);
        private object _configMessageFolderObj;
        private object _configMessageRequestObj;
        private volatile bool _closing;

        internal static HttpRequestsBackgroundPlugin Instance { get; private set; }

        // Cached config
        private List<Item> _folders = new List<Item>();
        private List<Item> _requests = new List<Item>();
        private readonly object _configLock = new object();

        public override Guid Id => HttpRequestsDefinition.BackgroundPluginId;
        public override string Name => "HTTP Requests Background";

        public override List<EnvironmentType> TargetEnvironments =>
            new List<EnvironmentType> { EnvironmentType.Service };

        public override void Init()
        {
            Instance = this;
            _log.Info("HTTP Requests background plugin initializing");

            _sysLog.Register();

            LoadConfig();

            // Listen for config changes on both folder and request kinds
            _configMessageFolderObj = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdAndRelatedKindFilter(
                    MessageId.Server.ConfigurationChangedIndication,
                    HttpRequestsDefinition.FolderKindId));

            _configMessageRequestObj = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdAndRelatedKindFilter(
                    MessageId.Server.ConfigurationChangedIndication,
                    HttpRequestsDefinition.RequestKindId));

            _cmh.Start();

            _log.Info("HTTP Requests background plugin initialized");
        }

        public override void Close()
        {
            _log.Info("HTTP Requests background plugin closing");
            _closing = true;

            if (_configMessageFolderObj != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_configMessageFolderObj);
                _configMessageFolderObj = null;
            }

            if (_configMessageRequestObj != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_configMessageRequestObj);
                _configMessageRequestObj = null;
            }

            _cmh.Close();
        }

        private void LoadConfig()
        {
            try
            {
                var folders = Configuration.Instance.GetItemConfigurations(
                    HttpRequestsDefinition.PluginId, null, HttpRequestsDefinition.FolderKindId);

                var allRequests = new List<Item>();
                foreach (var folder in folders)
                {
                    var requests = Configuration.Instance.GetItemConfigurations(
                        HttpRequestsDefinition.PluginId, folder, HttpRequestsDefinition.RequestKindId);
                    allRequests.AddRange(requests);
                }

                lock (_configLock)
                {
                    _folders = folders;
                    _requests = allRequests;
                }

                _log.Info($"Loaded config: {folders.Count} folders, {allRequests.Count} requests");
            }
            catch (Exception ex)
            {
                _log.Error($"Error loading config: {ex.Message}", ex);
            }
        }

        private object OnConfigurationChanged(Message message, FQID dest, FQID sender)
        {
            if (_closing) return null;

            _log.Info("Configuration changed, reloading");
            LoadConfig();
            return null;
        }

        public void HandleAction(FQID targetFqid, AnalyticsEvent triggeringEvent)
        {
            if (_closing) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ExecuteForItem(targetFqid, triggeringEvent);
                }
                catch (Exception ex)
                {
                    _log.Error($"Action execution error: {ex.Message}", ex);
                }
            });
        }

        private void ExecuteForItem(FQID targetFqid, AnalyticsEvent triggeringEvent)
        {
            var targetId = targetFqid.ObjectId;

            List<Item> requests;
            lock (_configLock)
            {
                requests = _requests.ToList();
            }

            var requestItem = requests.FirstOrDefault(r => r.FQID.ObjectId == targetId);
            if (requestItem != null)
            {
                ExecuteRequest(requestItem, triggeringEvent);
            }
            else
            {
                _log.Error($"Request item not found: {targetId}");
            }
        }

        private bool ExecuteRequest(Item requestItem, AnalyticsEvent triggeringEvent)
        {
            var method = GetProp(requestItem, "HttpMethod", "POST");
            var url = GetProp(requestItem, "Url", "");
            var payloadType = GetProp(requestItem, "PayloadType", "JSON");
            var userPayload = GetProp(requestItem, "UserPayload", "");
            var skipCert = GetProp(requestItem, "SkipCertValidation", "No") == "Yes";
            var includeEvent = GetProp(requestItem, "IncludeEventData", "Yes") != "No";
            var timeoutMs = 10000;
            int.TryParse(GetProp(requestItem, "TimeoutMs", "10000"), out timeoutMs);
            var authType = GetProp(requestItem, "AuthType", "None");

            var enabled = GetProp(requestItem, "Enabled", "Yes") != "No";
            if (!enabled)
            {
                _log.Info($"Request '{requestItem.Name}' is disabled, skipping");
                return true;
            }

            // Build body
            string body = null;
            if (payloadType != "None" && method != "GET" && method != "DELETE")
            {
                if (payloadType == "JSON")
                    body = BuildJsonPayload(userPayload, triggeringEvent, includeEvent);
                else
                    body = string.IsNullOrWhiteSpace(userPayload) ? null : userPayload;
            }

            var headers = ReadKeyValueProperties(requestItem, "Header_");

            // Read query params
            var queryParams = ReadKeyValueProperties(requestItem, "QueryParam_");

            var authUsername = GetProp(requestItem, "AuthUsername", "");
            var authPassword = GetProp(requestItem, "AuthPassword", "");
            var authToken = GetProp(requestItem, "AuthToken", "");

            _log.Info($"Executing '{requestItem.Name}': {method} {url} " +
                $"[auth={authType}, payload={payloadType}, timeout={timeoutMs}ms, " +
                $"headers={headers.Count}, params={queryParams.Count}, skipCert={skipCert}]");

            var config = new HttpRequestConfig
            {
                Name = requestItem.Name,
                HttpMethod = method,
                Url = url,
                PayloadType = payloadType,
                Body = body,
                Headers = headers.Count > 0 ? headers : null,
                QueryParams = queryParams.Count > 0 ? queryParams : null,
                SkipCertValidation = skipCert,
                TimeoutMs = timeoutMs,
                AuthType = authType,
                AuthUsername = authUsername,
                AuthPassword = authPassword,
                AuthToken = authToken
            };

            var result = HttpRequestExecutor.Execute(config);

            if (result.Success)
            {
                _log.Info($"Success '{requestItem.Name}': {method} {url} -> {result.StatusCode} ({result.ElapsedMs}ms)");
                _sysLog.RequestExecuted(method, url, result.StatusCode, result.ElapsedMs);
                FireEvent(requestItem, triggeringEvent, true, result);
            }
            else
            {
                _log.Error($"Failed '{requestItem.Name}': {method} {url} -> {result.Error} ({result.ElapsedMs}ms)");
                _sysLog.RequestFailed(method, url, result.Error);
                FireEvent(requestItem, triggeringEvent, false, result);
            }

            TransmitResult(requestItem, method, url, result);

            return result.Success;
        }

        private static Dictionary<string, string> ReadKeyValueProperties(Item item, string prefix)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var countKey = prefix + "Count";
            if (!item.Properties.ContainsKey(countKey)) return result;

            int count;
            if (!int.TryParse(item.Properties[countKey], out count)) return result;

            for (int i = 0; i < count; i++)
            {
                var kProp = $"{prefix}{i}_Key";
                var vProp = $"{prefix}{i}_Value";
                if (item.Properties.ContainsKey(kProp) && item.Properties.ContainsKey(vProp))
                {
                    var k = item.Properties[kProp];
                    if (!string.IsNullOrWhiteSpace(k))
                        result[k] = item.Properties[vProp];
                }
            }
            return result;
        }

        private string BuildJsonPayload(string userPayload, AnalyticsEvent triggeringEvent, bool includeEvent)
        {
            var hasUserPayload = !string.IsNullOrWhiteSpace(userPayload);
            string userJsonInner = "";

            if (hasUserPayload)
            {
                var trimmed = userPayload.Trim();
                if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                    userJsonInner = trimmed.Substring(1, trimmed.Length - 2).Trim();
                else
                    userJsonInner = $"\"data\": {EscapeJsonString(trimmed)}";
            }

            if (!includeEvent || triggeringEvent == null)
            {
                if (hasUserPayload)
                    return "{" + userJsonInner + "}";
                return "{}";
            }

            var eventJson = BuildEventJson(triggeringEvent);

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(userJsonInner))
                parts.Add(userJsonInner);
            parts.Add(eventJson);

            return "{" + string.Join(", ", parts) + "}";
        }

        private string BuildEventJson(AnalyticsEvent evt)
        {
            if (evt?.EventHeader == null)
                return "\"Event\": {}";

            var h = evt.EventHeader;
            var sb = new StringBuilder();
            sb.Append("\"Event\": {\"EventHeader\": {");
            sb.AppendFormat("\"ID\": {0}, ", EscapeJsonString(h.ID.ToString()));
            sb.AppendFormat("\"Timestamp\": {0}, ", EscapeJsonString(h.Timestamp.ToString("o")));
            sb.AppendFormat("\"Type\": {0}, ", EscapeJsonString(h.Type ?? ""));
            sb.AppendFormat("\"Name\": {0}, ", EscapeJsonString(h.Name ?? ""));
            sb.AppendFormat("\"Message\": {0}, ", EscapeJsonString(h.Message ?? ""));
            sb.AppendFormat("\"Priority\": {0}, ", h.Priority);
            sb.AppendFormat("\"CustomTag\": {0}", EscapeJsonString(h.CustomTag ?? ""));

            if (h.Source != null)
            {
                sb.Append(", \"Source\": {");
                sb.AppendFormat("\"Name\": {0}", EscapeJsonString(h.Source.Name ?? ""));
                if (h.Source.FQID != null)
                {
                    sb.AppendFormat(", \"FQID\": {{\"ObjectId\": {0}, \"Kind\": {1}}}",
                        EscapeJsonString(h.Source.FQID.ObjectId.ToString()),
                        EscapeJsonString(h.Source.FQID.Kind.ToString()));
                }
                sb.Append("}");
            }

            sb.Append("}}");

            try
            {
                var site = EnvironmentManager.Instance.MasterSite;
                if (site != null)
                {
                    sb.AppendFormat(", \"Site\": {{\"ServerHostname\": {0}, \"AbsoluteUri\": {1}}}",
                        EscapeJsonString(site.ServerId?.ServerHostname ?? ""),
                        EscapeJsonString(site.ServerId != null
                            ? $"{site.ServerId.ServerScheme}://{site.ServerId.ServerHostname}/"
                            : ""));
                }
            }
            catch { }

            return sb.ToString();
        }

        private static string EscapeJsonString(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private void FireEvent(Item requestItem, AnalyticsEvent triggeringEvent, bool success, HttpRequestResult result)
        {
            try
            {
                var eventTypeId = success
                    ? HttpRequestsDefinition.EvtRequestExecutedId
                    : HttpRequestsDefinition.EvtRequestFailedId;

                var method = GetProp(requestItem, "HttpMethod", "?");
                var url = GetProp(requestItem, "Url", "");

                var customTag = success
                    ? $"Request: {method} {url} | Status: {result.StatusCode} | Time: {result.ElapsedMs}ms"
                    : $"Request: {method} {url} | Error: {result.Error}";

                var header = new EventHeader
                {
                    ID = Guid.NewGuid(),
                    Class = "Operational",
                    Type = success ? "HttpRequestExecuted" : "HttpRequestFailed",
                    Timestamp = DateTime.Now,
                    Name = requestItem.Name,
                    Message = success ? "HTTP Request Executed" : "HTTP Request Failed",
                    CustomTag = customTag,
                    Priority = (ushort)(success ? 5 : 3),
                    Source = new EventSource
                    {
                        Name = requestItem.Name,
                        FQID = requestItem.FQID
                    }
                };

                var analyticsEvent = new AnalyticsEvent { EventHeader = header };

                EnvironmentManager.Instance.SendMessage(
                    new Message(MessageId.Server.NewEventCommand)
                    {
                        Data = analyticsEvent,
                        RelatedFQID = requestItem.FQID
                    });
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to fire event: {ex.Message}");
            }
        }

        private void TransmitResult(Item requestItem, string method, string url, HttpRequestResult result)
        {
            if (_cmh.MessageCommunication == null) return;

            try
            {
                var executionResult = new HttpExecutionResult
                {
                    RequestItemId = requestItem.FQID.ObjectId,
                    RequestName = requestItem.Name,
                    Url = url,
                    Method = method,
                    StatusCode = result.StatusCode,
                    Success = result.Success,
                    Error = result.Error,
                    ElapsedMs = result.ElapsedMs,
                    Timestamp = DateTime.UtcNow
                };

                _cmh.TransmitMessage(new Message(HttpMessageIds.ExecutionResult, executionResult));
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to transmit execution result: {ex.Message}");
            }
        }

        private static string GetProp(Item item, string key, string defaultValue)
        {
            return item.Properties.ContainsKey(key) ? item.Properties[key] : defaultValue;
        }
    }
}
