using System;
using System.Net.Http.Formatting;
using System.Web.Http;
using Microsoft.Owin.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Owin;
using Swashbuckle.Application;

namespace SCRemoteControl.Server
{
    class RemoteControlServer
    {
        private static readonly Lazy<RemoteControlServer> _instance = new Lazy<RemoteControlServer>(() => new RemoteControlServer());
        public static RemoteControlServer Instance => _instance.Value;

        private IDisposable _host;
        private readonly object _lock = new object();

        public bool IsListening { get; private set; }
        public string ListenUrl { get; private set; }
        public string ErrorMessage { get; private set; }

        public void Start()
        {
            lock (_lock)
            {
                Stop();

                try
                {
                    var scheme = SCRemoteControlConfig.UseTls ? "https" : "http";
                    var address = SCRemoteControlConfig.ListenAddress;
                    var port = SCRemoteControlConfig.Port;

                    var listenAddress = address == "0.0.0.0" ? "+" : address;
                    var url = $"{scheme}://{listenAddress}:{port}";
                    ListenUrl = $"{scheme}://{(address == "0.0.0.0" ? "localhost" : address)}:{port}";

                    _host = WebApp.Start(url, Configure);

                    IsListening = true;
                    ErrorMessage = null;

                    SCRemoteControlDefinition.Log.Info($"Server started at {url}");
                }
                catch (Exception ex)
                {
                    IsListening = false;
                    ErrorMessage = ex.Message;
                    SCRemoteControlDefinition.Log.Error("Failed to start HTTP server", ex);
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_host != null)
                {
                    try { _host.Dispose(); } catch { }
                    _host = null;
                }
                IsListening = false;
                ListenUrl = null;
            }
        }

        public void Restart()
        {
            SCRemoteControlConfig.Load();
            Start();
        }

        private void Configure(IAppBuilder app)
        {
            var config = new HttpConfiguration();

            // JSON formatting
            config.Formatters.Clear();
            config.Formatters.Add(new JsonMediaTypeFormatter
            {
                SerializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    NullValueHandling = NullValueHandling.Ignore
                }
            });

            // Attribute routing
            config.MapHttpAttributeRoutes();

            // CORS
            config.MessageHandlers.Add(new CorsHandler());

            // Auth
            config.MessageHandlers.Add(new TokenAuthHandler());

            // Swagger spec auto-generation (Swashbuckle) at /swagger/docs/v1
            config.EnableSwagger(c =>
            {
                c.SingleApiVersion("v1", "SC Remote Control API")
                    .Description("REST API for controlling Milestone XProtect Smart Client remotely. Use the Authorize button to set your Bearer token.");
                c.ApiKey("Bearer")
                    .Description("API token from SC Remote Control settings. Format: Bearer &lt;token&gt;")
                    .Name("Authorization")
                    .In("header");
            });

            // Modern Swagger UI 5.x (served from embedded resources)
            app.Use<SwaggerUiMiddleware>();

            app.UseWebApi(config);
        }
    }
}
