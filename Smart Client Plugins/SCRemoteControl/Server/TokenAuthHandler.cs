using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SCRemoteControl.Server
{
    class TokenAuthHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri.AbsolutePath;

            // Only require auth for /api/ endpoints
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                return base.SendAsync(request, cancellationToken);

            // Skip OPTIONS (CORS preflight)
            if (request.Method == HttpMethod.Options)
                return base.SendAsync(request, cancellationToken);

            string token = null;

            // Try standard Bearer scheme: "Authorization: Bearer <token>"
            if (request.Headers.Authorization != null)
            {
                if (string.Equals(request.Headers.Authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                    token = request.Headers.Authorization.Parameter;
                else
                    token = request.Headers.Authorization.Scheme; // raw value without scheme
            }

            // Fallback: raw header value (Swagger UI ApiKey sends "Authorization: Bearer <token>" as plain string)
            if (string.IsNullOrEmpty(token))
            {
                var raw = request.Headers.TryGetValues("Authorization", out var values)
                    ? string.Join("", values) : null;

                if (raw != null)
                {
                    raw = raw.Trim();
                    if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        token = raw.Substring(7).Trim();
                    else
                        token = raw;
                }
            }

            if (!SCRemoteControlConfig.ValidateToken(token))
            {
                var response = request.CreateResponse(HttpStatusCode.Unauthorized,
                    new { error = "Unauthorized. Provide a valid Bearer token in the Authorization header." });
                return Task.FromResult(response);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
