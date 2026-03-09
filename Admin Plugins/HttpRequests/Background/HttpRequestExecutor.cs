using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using CommunitySDK;

namespace HttpRequests.Background
{
    internal class HttpRequestConfig
    {
        public string Name;
        public string HttpMethod;
        public string Url;
        public string PayloadType;
        public string Body;
        public Dictionary<string, string> Headers;
        public Dictionary<string, string> QueryParams;
        public bool SkipCertValidation;
        public int TimeoutMs;
        public string AuthType;
        public string AuthUsername;
        public string AuthPassword;
        public string AuthToken;
    }

    internal class HttpRequestResult
    {
        public int StatusCode;
        public string ResponseBody;
        public long ElapsedMs;
        public bool Success;
        public string Error;
    }

    internal static class HttpRequestExecutor
    {
        private static readonly PluginLog _log = new PluginLog("HttpRequests.Executor");

        public static HttpRequestResult Execute(HttpRequestConfig config)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // Build URL with query params
                var url = BuildUrlWithParams(config.Url, config.QueryParams);

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = config.HttpMethod;
                request.Timeout = config.TimeoutMs > 0 ? config.TimeoutMs : 10000;
                request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);

                if (config.SkipCertValidation)
                {
                    request.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
                }

                // Authentication
                ApplyAuth(request, config);

                // Custom headers
                if (config.Headers != null)
                {
                    foreach (var kvp in config.Headers)
                    {
                        switch (kvp.Key.ToLowerInvariant())
                        {
                            case "content-type":
                                request.ContentType = kvp.Value;
                                break;
                            case "accept":
                                request.Accept = kvp.Value;
                                break;
                            case "user-agent":
                                request.UserAgent = kvp.Value;
                                break;
                            default:
                                request.Headers[kvp.Key] = kvp.Value;
                                break;
                        }
                    }
                }

                // Body
                if (!string.IsNullOrEmpty(config.Body) &&
                    config.HttpMethod != "GET" && config.HttpMethod != "DELETE")
                {
                    if (request.ContentType == null)
                        request.ContentType = GetContentType(config.PayloadType);

                    var bodyBytes = Encoding.UTF8.GetBytes(config.Body);
                    request.ContentLength = bodyBytes.Length;

                    using (var stream = request.GetRequestStream())
                    {
                        stream.Write(bodyBytes, 0, bodyBytes.Length);
                    }
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    sw.Stop();
                    string responseBody;
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        responseBody = reader.ReadToEnd();
                    }

                    return new HttpRequestResult
                    {
                        StatusCode = (int)response.StatusCode,
                        ResponseBody = responseBody,
                        ElapsedMs = sw.ElapsedMilliseconds,
                        Success = true
                    };
                }
            }
            catch (WebException wex)
            {
                sw.Stop();

                if (wex.Response is HttpWebResponse errorResponse)
                {
                    string errorBody;
                    using (var reader = new StreamReader(errorResponse.GetResponseStream(), Encoding.UTF8))
                    {
                        errorBody = reader.ReadToEnd();
                    }

                    return new HttpRequestResult
                    {
                        StatusCode = (int)errorResponse.StatusCode,
                        ResponseBody = errorBody,
                        ElapsedMs = sw.ElapsedMilliseconds,
                        Success = false,
                        Error = $"HTTP {(int)errorResponse.StatusCode}: {errorResponse.StatusDescription}"
                    };
                }

                return new HttpRequestResult
                {
                    StatusCode = 0,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Success = false,
                    Error = wex.Message
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new HttpRequestResult
                {
                    StatusCode = 0,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private static void ApplyAuth(HttpWebRequest request, HttpRequestConfig config)
        {
            if (string.IsNullOrEmpty(config.AuthType) || config.AuthType == "None")
                return;

            switch (config.AuthType)
            {
                case "Basic":
                    var credentials = (config.AuthUsername ?? "") + ":" + (config.AuthPassword ?? "");
                    var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
                    request.Headers["Authorization"] = "Basic " + basicToken;
                    break;

                case "Bearer":
                    request.Headers["Authorization"] = "Bearer " + (config.AuthToken ?? "");
                    break;

                case "Digest":
                    if (!string.IsNullOrEmpty(config.AuthUsername))
                    {
                        var credCache = new CredentialCache();
                        credCache.Add(new Uri(config.Url), "Digest",
                            new NetworkCredential(config.AuthUsername ?? "", config.AuthPassword ?? ""));
                        request.Credentials = credCache;
                        request.PreAuthenticate = true;
                    }
                    break;
            }
        }

        private static string GetContentType(string payloadType)
        {
            switch (payloadType)
            {
                case "JSON": return "application/json; charset=utf-8";
                case "XML": return "application/xml; charset=utf-8";
                case "Plain Text": return "text/plain; charset=utf-8";
                case "CSV": return "text/csv; charset=utf-8";
                default: return "text/plain; charset=utf-8";
            }
        }

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
    }
}
