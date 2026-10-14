using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LowLatencyVideoWall.Vapix
{
    /// <summary>
    /// What one param.cgi call came back with.
    ///
    /// Everything that can go wrong on the way to a camera (no route, wrong password,
    /// firmware without the parameter) ends up in here as a sentence a UI can show. A video
    /// wall has to keep working when one camera is down, so nothing in this layer throws.
    /// </summary>
    internal sealed class VapixResult
    {
        /// <summary>True when the request reached the camera and it answered with 200.</summary>
        internal bool Ok { get; set; }

        /// <summary>Readable reason the call did not reach or satisfy the camera.</summary>
        internal string Error { get; set; }

        /// <summary>Response body, trimmed. Kept for logging and for the update OK check.</summary>
        internal string Body { get; set; }

        /// <summary>Parameters the camera returned, keyed without the leading "root." prefix.</summary>
        internal Dictionary<string, string> Values { get; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The "# Error: Error -1 getting param" text the camera sent for a parameter it could
        /// not produce. Deliberately not treated as a failure: a missing parameter is how an
        /// Axis sensor reports that the current capture mode does not have it, and callers in
        /// this plugin rely on exactly that signal.
        /// </summary>
        internal string ParameterError { get; set; }

        internal static VapixResult Failed(string error)
        {
            return new VapixResult { Ok = false, Error = error, Body = "" };
        }
    }

    /// <summary>
    /// Talks to an Axis camera's /axis-cgi/param.cgi over HTTP digest.
    ///
    /// Instances are pooled per camera, because a fresh HttpClient per call would strand a
    /// socket in TIME_WAIT every time and throw away the digest nonce, which costs an extra
    /// 401 round trip. On a wall that re-reads every camera on a timer that adds up fast.
    /// Clients handed out by For() are owned by the pool, so callers must not dispose them;
    /// Shutdown() drops the lot when the plugin unloads.
    /// </summary>
    internal sealed class VapixClient : IDisposable
    {
        internal const int DefaultTimeoutSeconds = 5;

        private const string ParamCgi = "axis-cgi/param.cgi";

        private static readonly object PoolLock = new object();
        private static readonly Dictionary<string, VapixClient> Pool =
            new Dictionary<string, VapixClient>(StringComparer.OrdinalIgnoreCase);

        private readonly HttpClient _http;
        private readonly HttpClientHandler _handler;
        private readonly Uri _baseUri;
        private readonly string _userName;
        private readonly string _password;

        /// <summary>Host and port of the camera this client is bound to.</summary>
        internal string Authority { get { return _baseUri.Authority; } }

        private VapixClient(Uri baseUri, string userName, string password)
        {
            _baseUri = baseUri;
            _userName = userName;
            _password = password;

            // Axis answers param.cgi with a digest challenge by default. CredentialCache is
            // what makes HttpClient answer it, and unlike a bare NetworkCredential it pins
            // the password to the schemes we chose. Basic is registered too because a few
            // older models offer nothing else.
            var credentials = new CredentialCache();
            credentials.Add(_baseUri, "Digest", new NetworkCredential(userName, password));
            credentials.Add(_baseUri, "Basic", new NetworkCredential(userName, password));

            _handler = new HttpClientHandler
            {
                Credentials = credentials,

                // Without this, every request pays a 401 round trip before the real one.
                // With it the digest nonce is reused for the life of the connection.
                PreAuthenticate = true,

                // Cameras sit on the local network. A system proxy would either break the
                // call outright or add a hop this plugin exists to avoid.
                UseProxy = false,
                AllowAutoRedirect = false
            };

            if (string.Equals(_baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                // Cameras ship self signed certificates and Milestone does not validate them
                // when it pulls video either, so refusing them here would only mean the
                // status columns stay empty on an https site.
                try
                {
                    _handler.ServerCertificateCustomValidationCallback = (m, c, chain, errors) => true;
                }
                catch (Exception ex)
                {
                    LowLatencyVideoWallDefinition.Log.Info(
                        "VAPIX: could not install the certificate callback for " + Authority + ": " + ex.Message);
                }
            }

            _http = new HttpClient(_handler, true)
            {
                // Per call timeouts are enforced with a linked token below. This is only a
                // backstop for a camera that accepts the connection and then goes quiet
                // halfway through the body.
                Timeout = TimeSpan.FromSeconds(30)
            };
            _http.DefaultRequestHeaders.ExpectContinue = false;

            try
            {
                // net48 allows two connections per host, which serialises the status read of
                // a multi channel encoder behind a single socket pair.
                ServicePointManager.FindServicePoint(_baseUri).ConnectionLimit = 8;
            }
            catch
            {
            }
        }

        /// <summary>
        /// A client for this camera, reusing the pooled one when the credentials still match.
        /// The address may take any of the shapes the Milestone configuration hands out: a
        /// bare host, host:port, or a full URL with a trailing path. Returns null when no
        /// usable address can be made of it.
        /// </summary>
        internal static VapixClient For(string address, string userName, string password)
        {
            var baseUri = BuildBaseUri(address);
            if (baseUri == null) return null;

            // Axis ships with root, and Milestone leaves the field blank for some drivers.
            var user = string.IsNullOrEmpty(userName) ? "root" : userName;
            var pass = password ?? "";

            lock (PoolLock)
            {
                VapixClient existing;
                if (Pool.TryGetValue(baseUri.Authority, out existing))
                {
                    if (existing.Matches(baseUri, user, pass)) return existing;

                    // The password was rotated in Milestone. Credentials are baked into the
                    // handler, so the old client can never succeed again.
                    Pool.Remove(baseUri.Authority);
                    existing.Dispose();
                }

                var client = new VapixClient(baseUri, user, pass);
                Pool[baseUri.Authority] = client;
                return client;
            }
        }

        /// <summary>Drops every pooled client. Call when the plugin unloads.</summary>
        internal static void Shutdown()
        {
            lock (PoolLock)
            {
                foreach (var client in Pool.Values.ToList())
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch
                    {
                    }
                }
                Pool.Clear();
            }
        }

        private bool Matches(Uri baseUri, string userName, string password)
        {
            return string.Equals(_baseUri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_userName, userName, StringComparison.Ordinal)
                && string.Equals(_password, password, StringComparison.Ordinal);
        }

        /// <summary>
        /// Scheme, host and port of the camera, with any path dropped. Keeping the scheme and
        /// the port matters: Milestone stores hardware reached on a non standard port as part
        /// of the address, and WallCamera.Host throws the port away.
        /// </summary>
        internal static Uri BuildBaseUri(string address)
        {
            var text = (address ?? "").Trim();
            if (text.Length == 0) return null;

            if (text.IndexOf("://", StringComparison.Ordinal) < 0)
                text = "http://" + text;

            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri)) return null;

            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return null;

            if (string.IsNullOrEmpty(uri.Host)) return null;

            try
            {
                return new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;
            }
            catch
            {
                return null;
            }
        }

        // ----- reads -------------------------------------------------------------------

        /// <summary>
        /// action=list over one group, or over several comma separated groups. Keys come back
        /// without the "root." prefix. Null when the call failed; use ListResultAsync when the
        /// reason matters.
        /// </summary>
        internal async Task<Dictionary<string, string>> ListAsync(string group,
            CancellationToken token = default(CancellationToken),
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            var result = await ListResultAsync(group, token, timeoutSeconds).ConfigureAwait(false);
            return result.Ok ? result.Values : null;
        }

        /// <summary>action=list, with the failure reason kept.</summary>
        internal Task<VapixResult> ListResultAsync(string group,
            CancellationToken token = default(CancellationToken),
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            var query = new StringBuilder("action=list");
            if (!string.IsNullOrWhiteSpace(group))
                query.Append("&group=").Append(CleanGroup(group));

            return SendAsync(query.ToString(), ResponseShape.Parameters, token, timeoutSeconds);
        }

        /// <summary>
        /// action=listdefinitions over one group, flattened to allowed value against human
        /// name. This is how a capture mode number turns into
        /// "1920x1080 (16:9) @ 50/60 fps"; the plain parameter list only carries the number.
        /// </summary>
        internal async Task<Dictionary<string, string>> ListDefinitionsAsync(string group,
            CancellationToken token = default(CancellationToken),
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            var result = await ListDefinitionsResultAsync(group, token, timeoutSeconds).ConfigureAwait(false);
            return result.Ok ? result.Values : null;
        }

        /// <summary>action=listdefinitions, with the failure reason kept.</summary>
        internal Task<VapixResult> ListDefinitionsResultAsync(string group,
            CancellationToken token = default(CancellationToken),
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            var query = new StringBuilder("action=listdefinitions&listformat=xmlschema");
            if (!string.IsNullOrWhiteSpace(group))
                query.Append("&group=").Append(CleanGroup(group));

            return SendAsync(query.ToString(), ResponseShape.Definitions, token, timeoutSeconds);
        }

        // ----- writes ------------------------------------------------------------------

        /// <summary>
        /// action=update. True only when the camera answered the bare "OK" body that means it
        /// took the change. Anything else, including a 200 carrying an error line, is false.
        /// </summary>
        internal async Task<bool> UpdateAsync(IDictionary<string, string> values,
            CancellationToken token = default(CancellationToken),
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            var result = await UpdateResultAsync(values, token, timeoutSeconds).ConfigureAwait(false);
            return result.Ok;
        }

        /// <summary>action=update, with the camera's own complaint kept when it refuses.</summary>
        internal async Task<VapixResult> UpdateResultAsync(IDictionary<string, string> values,
            CancellationToken token = default(CancellationToken),
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (values == null || values.Count == 0)
                return VapixResult.Failed("Nothing to update");

            var query = new StringBuilder("action=update");
            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                query.Append('&')
                     .Append(WriteKey(pair.Key))
                     .Append('=')
                     .Append(Uri.EscapeDataString(pair.Value ?? ""));
            }

            var result = await SendAsync(query.ToString(), ResponseShape.Raw, token, timeoutSeconds)
                .ConfigureAwait(false);
            if (!result.Ok) return result;

            if (!IsOkBody(result.Body))
            {
                // A camera that dislikes a value answers 200 with an error line instead of an
                // HTTP error, so the body is the only place the reason lives.
                result.Ok = false;
                result.Error = string.IsNullOrEmpty(result.Body)
                    ? "The camera answered the update with an empty body"
                    : "The camera refused the change: " + Flatten(result.Body);
            }

            return result;
        }

        private static bool IsOkBody(string body)
        {
            return !string.IsNullOrEmpty(body)
                && string.Equals(body.Trim(), "OK", StringComparison.OrdinalIgnoreCase);
        }

        // ----- transport ---------------------------------------------------------------

        private enum ResponseShape
        {
            Raw,
            Parameters,
            Definitions
        }

        private async Task<VapixResult> SendAsync(string query, ResponseShape shape,
            CancellationToken token, int timeoutSeconds)
        {
            var seconds = timeoutSeconds > 0 ? timeoutSeconds : DefaultTimeoutSeconds;
            var uri = new Uri(_baseUri, ParamCgi + "?" + query);

            using (var call = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                // The timeout sits on the linked source rather than on HttpClient so a caller
                // cancelling a whole overview refresh is told apart from a camera that simply
                // never answered.
                call.CancelAfter(TimeSpan.FromSeconds(seconds));

                try
                {
                    using (var response = await _http
                        .GetAsync(uri, HttpCompletionOption.ResponseContentRead, call.Token)
                        .ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        body = (body ?? "").Trim();

                        if (!response.IsSuccessStatusCode)
                            return Fail(uri, Describe(response, body));

                        var result = new VapixResult { Ok = true, Body = body };

                        if (shape == ResponseShape.Parameters) ParseParameters(body, result);
                        else if (shape == ResponseShape.Definitions) ParseDefinitions(body, result);

                        return result;
                    }
                }
                catch (OperationCanceledException)
                {
                    return Fail(uri, token.IsCancellationRequested
                        ? "The request was cancelled"
                        : "No answer from " + Authority + " within " + seconds + " s");
                }
                catch (HttpRequestException ex)
                {
                    return Fail(uri, "Cannot reach " + Authority + ": " + Innermost(ex));
                }
                catch (Exception ex)
                {
                    // Anything left over (a disposed handler, a name resolution failure that
                    // surfaces as something else) still comes back as text. Nothing from this
                    // layer is allowed to throw into Milestone.
                    return Fail(uri, ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static VapixResult Fail(Uri uri, string error)
        {
            // Info, not Error: with one camera offline on a polling overview this line would
            // otherwise fill MIPLog on every refresh.
            LowLatencyVideoWallDefinition.Log.Info(
                "VAPIX " + uri.Host + " " + uri.Query + " failed: " + error);
            return VapixResult.Failed(error);
        }

        private static string Describe(HttpResponseMessage response, string body)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return "Authentication failed (401). Check the camera user name and password in Milestone.";
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return "This camera user is not allowed to read or change parameters (403).";
            if (response.StatusCode == HttpStatusCode.NotFound)
                return "param.cgi was not found (404). This does not look like an Axis camera.";

            var text = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                       + " " + response.ReasonPhrase;
            return string.IsNullOrEmpty(body) ? text : text + ": " + Flatten(body);
        }

        private static string Innermost(Exception ex)
        {
            var current = ex;
            while (current.InnerException != null) current = current.InnerException;
            return current.Message;
        }

        private static string Flatten(string text)
        {
            var one = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return one.Length > 300 ? one.Substring(0, 300) + "..." : one;
        }

        /// <summary>
        /// Group names are dotted parameter paths, optionally comma separated. Escaping them
        /// would turn the separators into %2C and %2E, so they are filtered instead.
        /// </summary>
        private static string CleanGroup(string group)
        {
            var builder = new StringBuilder(group.Length);
            foreach (var c in group)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == ',' || c == '_' || c == '-')
                    builder.Append(c);
            }
            return builder.ToString();
        }

        /// <summary>
        /// param.cgi wants the root prefixed form on a write, while a read hands the keys back
        /// the same way. Reads here strip the prefix and writes put it back, so every caller
        /// in the plugin can spell a parameter one single way.
        /// </summary>
        private static string WriteKey(string key)
        {
            var clean = CleanGroup(key.Trim());
            return clean.StartsWith("root.", StringComparison.OrdinalIgnoreCase)
                ? clean
                : "root." + clean;
        }

        private static void ParseParameters(string body, VapixResult result)
        {
            if (string.IsNullOrEmpty(body)) return;

            List<string> errors = null;

            foreach (var raw in body.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (line[0] == '#')
                {
                    // "# Error: Error -1 getting param" for one parameter out of a group the
                    // camera otherwise answered. Collected, never fatal.
                    if (errors == null) errors = new List<string>();
                    errors.Add(line.TrimStart('#').Trim());
                    continue;
                }

                var split = line.IndexOf('=');
                if (split < 0)
                {
                    if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (errors == null) errors = new List<string>();
                        errors.Add(line);
                    }
                    continue;
                }

                // Values can contain '=' themselves, so only the first one separates.
                var key = line.Substring(0, split).Trim();
                var value = line.Substring(split + 1).Trim();

                if (key.StartsWith("root.", StringComparison.OrdinalIgnoreCase))
                    key = key.Substring(5);

                if (key.Length > 0) result.Values[key] = value;
            }

            if (errors != null) result.ParameterError = string.Join("; ", errors);
        }

        /// <summary>
        /// Flattens the xmlschema definition document to allowed value against human name.
        /// Entries are taken from anywhere in the document so the same code works whether the
        /// group asked for was the enumerated parameter itself or one of its parents.
        /// </summary>
        private static void ParseDefinitions(string body, VapixResult result)
        {
            if (string.IsNullOrEmpty(body))
            {
                result.Ok = false;
                result.Error = "The camera returned no parameter definitions";
                return;
            }

            try
            {
                var document = XDocument.Parse(body);
                foreach (var entry in document.Descendants())
                {
                    if (!string.Equals(entry.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = Attribute(entry, "value");
                    if (string.IsNullOrEmpty(value)) continue;

                    var nice = Attribute(entry, "niceValue");
                    result.Values[value] = string.IsNullOrEmpty(nice) ? value : nice;
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = "Could not read the parameter definitions: " + ex.Message;
            }
        }

        private static string Attribute(XElement element, string name)
        {
            foreach (var attribute in element.Attributes())
            {
                if (string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                    return attribute.Value;
            }
            return null;
        }

        public void Dispose()
        {
            try
            {
                _http.Dispose();
            }
            catch
            {
            }
        }
    }
}
