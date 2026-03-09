using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using HttpRequests.Background;
using VideoOS.Platform;

namespace HttpRequests.Admin
{
    public partial class HttpRequestUserControl : UserControl
    {
        internal event EventHandler ConfigurationChangedByUser;
        internal event EventHandler DuplicateRequested;

        public HttpRequestUserControl()
        {
            InitializeComponent();
            _lblTestStatus.SizeChanged += (s, e) =>
            {
                _txtTestResponse.Top = _lblTestStatus.Bottom + 4;
                _txtTestResponse.Height = _grpTestResult.ClientSize.Height - _txtTestResponse.Top - 4;
            };
        }

        public string DisplayName => _txtName.Text;

        public void FillContent(Item item)
        {
            if (item == null)
            {
                ClearContent();
                return;
            }

            _txtName.Text = item.Name;

            _cboMethod.SelectedItem = GetProp(item, "HttpMethod", "POST");

            _txtUrl.Text = GetProp(item, "Url", "");

            _cboPayloadType.SelectedItem = GetProp(item, "PayloadType", "JSON");

            _txtPayload.Text = GetProp(item, "UserPayload", "");

            _chkEnabled.Checked = GetProp(item, "Enabled", "Yes") != "No";

            _chkSkipCertValidation.Checked = GetProp(item, "SkipCertValidation", "No") == "Yes";

            _chkIncludeEventData.Checked = GetProp(item, "IncludeEventData", "Yes") != "No";

            _txtTimeout.Text = GetProp(item, "TimeoutMs", "10000");

            // Auth
            var authType = GetProp(item, "AuthType", "None");
            _cboAuthType.SelectedItem = authType;

            _txtAuthUsername.Text = GetProp(item, "AuthUsername", "");
            _txtAuthPassword.Text = GetProp(item, "AuthPassword", "");
            _txtAuthToken.Text = GetProp(item, "AuthToken", "");

            _dgvQueryParams.Rows.Clear();
            LoadKeyValueGrid(_dgvQueryParams, item, "QueryParam_");

            _dgvHeaders.Rows.Clear();
            LoadKeyValueGrid(_dgvHeaders, item, "Header_");

            UpdateBodyVisibility();
            UpdatePayloadUI();
            UpdateAuthUI();
        }

        public string ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
                return "Please enter a name for the request.";

            var url = _txtUrl.Text.Trim();
            if (string.IsNullOrEmpty(url) || url == "https://")
                return "Please enter a URL.";

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "URL must start with http:// or https://";

            if (!string.IsNullOrWhiteSpace(_txtTimeout.Text) &&
                !int.TryParse(_txtTimeout.Text.Trim(), out _))
                return "Timeout must be a number (milliseconds).";

            var authType = _cboAuthType.SelectedItem?.ToString() ?? "None";
            if (authType == "Basic" || authType == "Digest")
            {
                if (string.IsNullOrWhiteSpace(_txtAuthUsername.Text))
                    return $"{authType} auth requires a username.";
            }
            else if (authType == "Bearer")
            {
                if (string.IsNullOrWhiteSpace(_txtAuthToken.Text))
                    return "Bearer auth requires a token.";
            }

            // Validate JSON payload
            var payloadType = _cboPayloadType.SelectedItem?.ToString() ?? "None";
            if (payloadType == "JSON" && !string.IsNullOrWhiteSpace(_txtPayload.Text))
            {
                if (!IsValidJson(_txtPayload.Text.Trim()))
                    return "Payload is not valid JSON. Please check your syntax.";
            }

            return null;
        }

        public void UpdateItem(Item item)
        {
            if (item == null) return;

            item.Name = _txtName.Text;
            item.Properties["HttpMethod"] = _cboMethod.SelectedItem?.ToString() ?? "POST";
            item.Properties["Url"] = _txtUrl.Text.Trim();
            item.Properties["PayloadType"] = _cboPayloadType.SelectedItem?.ToString() ?? "JSON";
            item.Properties["UserPayload"] = _txtPayload.Text;
            item.Properties["Enabled"] = _chkEnabled.Checked ? "Yes" : "No";
            item.Properties["SkipCertValidation"] = _chkSkipCertValidation.Checked ? "Yes" : "No";
            item.Properties["IncludeEventData"] = _chkIncludeEventData.Checked ? "Yes" : "No";
            item.Properties["TimeoutMs"] = _txtTimeout.Text.Trim();
            item.Properties["AuthType"] = _cboAuthType.SelectedItem?.ToString() ?? "None";
            item.Properties["AuthUsername"] = _txtAuthUsername.Text;
            item.Properties["AuthPassword"] = _txtAuthPassword.Text;
            item.Properties["AuthToken"] = _txtAuthToken.Text;

            // Save headers
            SaveKeyValueGrid(_dgvHeaders, item, "Header_");

            // Save query params
            SaveKeyValueGrid(_dgvQueryParams, item, "QueryParam_");

            // Clean up legacy properties
            item.Properties.Remove("AuthValue");
            item.Properties.Remove("Headers");
        }

        public void ClearContent()
        {
            _txtName.Text = "";
            _cboMethod.SelectedItem = "POST";
            _txtUrl.Text = "";
            _cboPayloadType.SelectedItem = "JSON";
            _txtPayload.Text = "";
            _chkEnabled.Checked = true;
            _chkSkipCertValidation.Checked = false;
            _chkIncludeEventData.Checked = true;
            _txtTimeout.Text = "10000";
            _cboAuthType.SelectedItem = "None";
            _txtAuthUsername.Text = "";
            _txtAuthPassword.Text = "";
            _txtAuthToken.Text = "";
            _dgvQueryParams.Rows.Clear();
            _dgvHeaders.Rows.Clear();
            _lblTestStatus.Text = "";
            _txtTestResponse.Text = "";
            UpdateBodyVisibility();
            UpdatePayloadUI();
            UpdateAuthUI();
        }

        // ─── Duplicate ──────────────────────────────────────

        private void OnDuplicateClick(object sender, EventArgs e)
        {
            DuplicateRequested?.Invoke(this, EventArgs.Empty);
        }

        // ─── Test ───────────────────────────────────────────

        private void OnTestClick(object sender, EventArgs e)
        {
            var error = ValidateInput();
            if (error != null)
            {
                ShowTestResult(false, 0, 0, error, null);
                return;
            }

            var method = _cboMethod.SelectedItem?.ToString() ?? "POST";
            var url = _txtUrl.Text.Trim();
            var payloadType = _cboPayloadType.SelectedItem?.ToString() ?? "JSON";
            var userPayload = _txtPayload.Text;
            var skipCert = _chkSkipCertValidation.Checked;
            var timeoutMs = 10000;
            int.TryParse(_txtTimeout.Text.Trim(), out timeoutMs);
            var authType = _cboAuthType.SelectedItem?.ToString() ?? "None";

            // Build URL with query params
            var queryParams = GetKeyValuePairs(_dgvQueryParams);
            if (queryParams.Count > 0)
                url = BuildUrlWithParams(url, queryParams);

            // Build body (no event data for test - just user payload)
            string body = null;
            if (payloadType != "None" && method != "GET" && method != "DELETE")
                body = string.IsNullOrWhiteSpace(userPayload) ? null : userPayload;

            // Parse headers
            var headers = GetKeyValuePairs(_dgvHeaders);

            _btnTest.Enabled = false;
            _btnTest.Text = "Sending...";
            _lblTestStatus.Text = "";
            _txtTestResponse.Text = "";

            var config = new HttpRequestConfig
            {
                Name = "Test",
                HttpMethod = method,
                Url = url,
                PayloadType = payloadType,
                Body = body,
                Headers = headers.Count > 0 ? headers : null,
                SkipCertValidation = skipCert,
                TimeoutMs = timeoutMs,
                AuthType = authType,
                AuthUsername = _txtAuthUsername.Text,
                AuthPassword = _txtAuthPassword.Text,
                AuthToken = _txtAuthToken.Text
            };

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = HttpRequestExecutor.Execute(config);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        ShowTestResult(result.Success, result.StatusCode, result.ElapsedMs,
                            result.Error, result.ResponseBody);
                        _btnTest.Enabled = true;
                        _btnTest.Text = "Send Test";
                    }));
                }
                catch (ObjectDisposedException) { }
            });
        }

        private void ShowTestResult(bool success, int statusCode, long elapsedMs, string error, string responseBody)
        {
            if (success)
            {
                _lblTestStatus.ForeColor = Color.Green;
                _lblTestStatus.Text = $"{statusCode} {GetHttpStatusText(statusCode)}  ({elapsedMs}ms)";
            }
            else if (statusCode > 0)
            {
                _lblTestStatus.ForeColor = Color.FromArgb(200, 120, 0);
                _lblTestStatus.Text = $"{statusCode} {GetHttpStatusText(statusCode)}  ({elapsedMs}ms)";
            }
            else
            {
                _lblTestStatus.ForeColor = Color.Red;
                _lblTestStatus.Text = SimplifyError(error) ?? "Request failed";
            }

            if (!string.IsNullOrEmpty(responseBody))
                _txtTestResponse.Text = TryFormatJson(responseBody);
            else if (!string.IsNullOrEmpty(error))
                _txtTestResponse.Text = error;
            else
                _txtTestResponse.Text = "(empty response body)";

        }

        // ─── UI Updates ─────────────────────────────────────

        private void OnMethodChanged(object sender, EventArgs e)
        {
            UpdateBodyVisibility();
            OnUserChange(sender, e);
        }

        private void OnPayloadTypeChanged(object sender, EventArgs e)
        {
            UpdatePayloadUI();
            OnUserChange(sender, e);
        }

        private void UpdateBodyVisibility()
        {
            var method = _cboMethod.SelectedItem?.ToString() ?? "POST";
            bool showBody = method != "GET" && method != "DELETE";
            _grpBody.Visible = showBody;
            _tabControl.Top = showBody ? _grpBody.Bottom + 6 : _grpBody.Top;
        }

        private void UpdatePayloadUI()
        {
            var payloadType = _cboPayloadType.SelectedItem?.ToString() ?? "None";
            bool hasBody = payloadType != "None";
            bool isJson = payloadType == "JSON";

            _txtPayload.Visible = hasBody;
            _chkIncludeEventData.Visible = isJson;
        }

        private void OnAuthTypeChanged(object sender, EventArgs e)
        {
            UpdateAuthUI();
            OnUserChange(sender, e);
        }

        private void UpdateAuthUI()
        {
            var authType = _cboAuthType.SelectedItem?.ToString() ?? "None";
            bool showCredentials = authType == "Basic" || authType == "Digest";
            bool showToken = authType == "Bearer";

            _lblAuthUsername.Visible = showCredentials;
            _txtAuthUsername.Visible = showCredentials;
            _lblAuthPassword.Visible = showCredentials;
            _txtAuthPassword.Visible = showCredentials;
            _lblAuthToken.Visible = showToken;
            _txtAuthToken.Visible = showToken;
        }

        internal void OnUserChange(object sender, EventArgs e)
        {
            ConfigurationChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        private void OnGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            OnUserChange(sender, EventArgs.Empty);
        }

        private void OnRemoveQueryParam(object sender, EventArgs e)
        {
            RemoveSelectedRow(_dgvQueryParams);
            OnUserChange(sender, e);
        }

        private void OnRemoveHeader(object sender, EventArgs e)
        {
            RemoveSelectedRow(_dgvHeaders);
            OnUserChange(sender, e);
        }

        private static void RemoveSelectedRow(DataGridView dgv)
        {
            if (dgv.CurrentRow != null && !dgv.CurrentRow.IsNewRow)
                dgv.Rows.Remove(dgv.CurrentRow);
        }

        // ─── Key-Value Grid Helpers ─────────────────────────

        private static void LoadKeyValueGrid(DataGridView dgv, Item item, string prefix)
        {
            var countKey = prefix + "Count";
            if (!item.Properties.ContainsKey(countKey)) return;

            int count;
            if (!int.TryParse(item.Properties[countKey], out count)) return;

            for (int i = 0; i < count; i++)
            {
                var kProp = $"{prefix}{i}_Key";
                var vProp = $"{prefix}{i}_Value";
                var k = item.Properties.ContainsKey(kProp) ? item.Properties[kProp] : "";
                var v = item.Properties.ContainsKey(vProp) ? item.Properties[vProp] : "";
                if (!string.IsNullOrWhiteSpace(k))
                    dgv.Rows.Add(k, v);
            }
        }

        private static void SaveKeyValueGrid(DataGridView dgv, Item item, string prefix)
        {
            // Clear old entries
            var keysToRemove = new List<string>();
            foreach (var key in item.Properties.Keys)
            {
                if (key.StartsWith(prefix))
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                item.Properties.Remove(key);

            int count = 0;
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                var k = row.Cells[0].Value?.ToString();
                var v = row.Cells[1].Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(k))
                {
                    item.Properties[$"{prefix}{count}_Key"] = k;
                    item.Properties[$"{prefix}{count}_Value"] = v;
                    count++;
                }
            }
            item.Properties[$"{prefix}Count"] = count.ToString();
        }

        private static Dictionary<string, string> GetKeyValuePairs(DataGridView dgv)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                var k = row.Cells[0].Value?.ToString();
                var v = row.Cells[1].Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(k))
                    result[k] = v;
            }
            return result;
        }

        // ─── URL Builder ────────────────────────────────────

        private static string BuildUrlWithParams(string baseUrl, Dictionary<string, string> queryParams)
        {
            if (queryParams == null || queryParams.Count == 0)
                return baseUrl;

            var sb = new StringBuilder(baseUrl);
            sb.Append(baseUrl.Contains("?") ? "&" : "?");
            bool first = true;
            foreach (var kvp in queryParams)
            {
                if (!first) sb.Append("&");
                sb.Append(Uri.EscapeDataString(kvp.Key));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(kvp.Value));
                first = false;
            }
            return sb.ToString();
        }

        // ─── JSON Validation (recursive descent) ────────────

        private static bool IsValidJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            int pos = 0;
            bool ok = JsonParseValue(text, ref pos);
            if (!ok) return false;
            JsonSkipWhitespace(text, ref pos);
            return pos == text.Length;
        }

        private static void JsonSkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\r' || s[pos] == '\n'))
                pos++;
        }

        private static bool JsonParseValue(string s, ref int pos)
        {
            JsonSkipWhitespace(s, ref pos);
            if (pos >= s.Length) return false;
            var c = s[pos];
            if (c == '"') return JsonParseString(s, ref pos);
            if (c == '{') return JsonParseObject(s, ref pos);
            if (c == '[') return JsonParseArray(s, ref pos);
            if (c == 't') return JsonParseLiteral(s, ref pos, "true");
            if (c == 'f') return JsonParseLiteral(s, ref pos, "false");
            if (c == 'n') return JsonParseLiteral(s, ref pos, "null");
            if (c == '-' || (c >= '0' && c <= '9')) return JsonParseNumber(s, ref pos);
            return false;
        }

        private static bool JsonParseObject(string s, ref int pos)
        {
            if (pos >= s.Length || s[pos] != '{') return false;
            pos++;
            JsonSkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; return true; }
            while (true)
            {
                JsonSkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != '"') return false;
                if (!JsonParseString(s, ref pos)) return false;
                JsonSkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') return false;
                pos++;
                if (!JsonParseValue(s, ref pos)) return false;
                JsonSkipWhitespace(s, ref pos);
                if (pos >= s.Length) return false;
                if (s[pos] == '}') { pos++; return true; }
                if (s[pos] != ',') return false;
                pos++;
            }
        }

        private static bool JsonParseArray(string s, ref int pos)
        {
            if (pos >= s.Length || s[pos] != '[') return false;
            pos++;
            JsonSkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; return true; }
            while (true)
            {
                if (!JsonParseValue(s, ref pos)) return false;
                JsonSkipWhitespace(s, ref pos);
                if (pos >= s.Length) return false;
                if (s[pos] == ']') { pos++; return true; }
                if (s[pos] != ',') return false;
                pos++;
            }
        }

        private static bool JsonParseString(string s, ref int pos)
        {
            if (pos >= s.Length || s[pos] != '"') return false;
            pos++;
            while (pos < s.Length)
            {
                var c = s[pos];
                if (c == '\\') { pos += 2; continue; }
                if (c == '"') { pos++; return true; }
                if (c < 0x20) return false;
                pos++;
            }
            return false;
        }

        private static bool JsonParseNumber(string s, ref int pos)
        {
            int start = pos;
            if (pos < s.Length && s[pos] == '-') pos++;
            if (pos >= s.Length || s[pos] < '0' || s[pos] > '9') return false;
            if (s[pos] == '0') pos++;
            else { while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++; }
            if (pos < s.Length && s[pos] == '.')
            {
                pos++;
                if (pos >= s.Length || s[pos] < '0' || s[pos] > '9') return false;
                while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
            }
            if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
            {
                pos++;
                if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                if (pos >= s.Length || s[pos] < '0' || s[pos] > '9') return false;
                while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
            }
            return pos > start;
        }

        private static bool JsonParseLiteral(string s, ref int pos, string literal)
        {
            if (pos + literal.Length > s.Length) return false;
            for (int i = 0; i < literal.Length; i++)
                if (s[pos + i] != literal[i]) return false;
            pos += literal.Length;
            return true;
        }

        private static string GetProp(Item item, string key, string defaultValue)
        {
            return item.Properties.ContainsKey(key) ? item.Properties[key] : defaultValue;
        }

        // ─── Formatting Helpers ─────────────────────────────

        private static string SimplifyError(string error)
        {
            if (string.IsNullOrEmpty(error)) return error;

            if (error.Contains("trust relationship") || error.Contains("SSL/TLS"))
                return "SSL certificate validation failed";
            if (error.Contains("Unable to connect"))
                return "Connection refused";
            if (error.Contains("timed out") || error.Contains("Timeout"))
                return "Request timed out";
            if (error.Contains("name could not be resolved") || error.Contains("No such host"))
                return "DNS lookup failed";

            return error;
        }

        private static string GetHttpStatusText(int code)
        {
            switch (code)
            {
                case 200: return "OK";
                case 201: return "Created";
                case 204: return "No Content";
                case 301: return "Moved Permanently";
                case 302: return "Found";
                case 304: return "Not Modified";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 408: return "Request Timeout";
                case 409: return "Conflict";
                case 415: return "Unsupported Media Type";
                case 422: return "Unprocessable Entity";
                case 429: return "Too Many Requests";
                case 500: return "Internal Server Error";
                case 502: return "Bad Gateway";
                case 503: return "Service Unavailable";
                case 504: return "Gateway Timeout";
                default: return "Error";
            }
        }

        private static string TryFormatJson(string raw)
        {
            try
            {
                var trimmed = raw.Trim();
                if ((!trimmed.StartsWith("{") && !trimmed.StartsWith("[")) || trimmed.Length < 2)
                    return raw;

                var sb = new StringBuilder();
                var indent = 0;
                var inStr = false;
                var escaped = false;

                foreach (var ch in trimmed)
                {
                    if (escaped) { sb.Append(ch); escaped = false; continue; }
                    if (ch == '\\' && inStr) { sb.Append(ch); escaped = true; continue; }
                    if (ch == '"') { inStr = !inStr; sb.Append(ch); continue; }
                    if (inStr) { sb.Append(ch); continue; }

                    switch (ch)
                    {
                        case '{':
                        case '[':
                            sb.Append(ch);
                            sb.AppendLine();
                            indent++;
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case '}':
                        case ']':
                            sb.AppendLine();
                            indent--;
                            sb.Append(new string(' ', indent * 2));
                            sb.Append(ch);
                            break;
                        case ',':
                            sb.Append(ch);
                            sb.AppendLine();
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case ':':
                            sb.Append(": ");
                            break;
                        case ' ':
                        case '\t':
                        case '\r':
                        case '\n':
                            break;
                        default:
                            sb.Append(ch);
                            break;
                    }
                }
                return sb.ToString();
            }
            catch
            {
                return raw;
            }
        }
    }
}
