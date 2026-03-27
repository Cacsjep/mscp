using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SCRemoteControl.Server
{
    class CorsHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Options)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                AddCorsHeaders(response);
                return Task.FromResult(response);
            }

            return base.SendAsync(request, cancellationToken).ContinueWith(task =>
            {
                var response = task.Result;
                AddCorsHeaders(response);
                return response;
            }, cancellationToken);
        }

        private static void AddCorsHeaders(HttpResponseMessage response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Authorization, Content-Type");
        }
    }
}
