using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SCRemoteControl.Server
{
    class CorsHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Options)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                AddCorsHeaders(response, request);
                return response;
            }

            var resp = await base.SendAsync(request, cancellationToken);
            AddCorsHeaders(resp, request);
            return resp;
        }

        private static void AddCorsHeaders(HttpResponseMessage response, HttpRequestMessage request)
        {
            // Only allow same-origin or explicitly configured origins
            // Do NOT use wildcard with Authorization header - it enables cross-origin attacks
            var origin = request.Headers.Contains("Origin")
                ? string.Join("", request.Headers.GetValues("Origin"))
                : null;

            if (origin != null && IsAllowedOrigin(origin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Authorization, Content-Type");
                response.Headers.Add("Vary", "Origin");
            }
        }

        private static bool IsAllowedOrigin(string origin)
        {
            // Allow requests from the server itself (Swagger UI accessed via any of its addresses)
            var listenUrl = RemoteControlServer.Instance.ListenUrl;
            if (listenUrl != null && origin.TrimEnd('/') == listenUrl.TrimEnd('/'))
                return true;

            // Allow localhost variants
            var port = SCRemoteControlConfig.Port;
            if (origin == $"http://localhost:{port}" || origin == $"https://localhost:{port}"
                || origin == $"http://127.0.0.1:{port}" || origin == $"https://127.0.0.1:{port}")
                return true;

            return false;
        }
    }
}
