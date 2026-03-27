using System;
using System.Net;
using System.Net.Http.Formatting;
using System.Web.Http;
using Microsoft.Owin.Builder;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Nowin;
using Owin;
using Swashbuckle.Application;

namespace SCRemoteControl.Server
{
    class RemoteControlServer
    {
        private static readonly Lazy<RemoteControlServer> _instance = new Lazy<RemoteControlServer>(() => new RemoteControlServer());
        public static RemoteControlServer Instance => _instance.Value;

        private INowinServer _server;
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
                    var ip = address == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(address);

                    ListenUrl = $"{scheme}://{address}:{port}";

                    var owinApp = new AppBuilder();
                    Configure(owinApp);
                    var appFunc = owinApp.Build();

                    var builder = ServerBuilder.New()
                        .SetAddress(ip)
                        .SetPort(port)
                        .SetOwinApp(appFunc);

                    _server = builder.Build();
                    _server.Start();

                    IsListening = true;
                    ErrorMessage = null;

                    SCRemoteControlDefinition.Log.Info($"Server started at {ListenUrl}");
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
                if (_server != null)
                {
                    try { _server.Dispose(); } catch { }
                    _server = null;
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

            config.MapHttpAttributeRoutes();
            config.MessageHandlers.Add(new CorsHandler());
            config.MessageHandlers.Add(new TokenAuthHandler());

            // Swagger spec auto-generation at /swagger/docs/v1
            config.EnableSwagger(c =>
            {
                c.SingleApiVersion("v1", "Remote Control API")
                    .Description("REST API for controlling Milestone XProtect Smart Client remotely. Use the Authorize button to set your Bearer token.");
                c.ApiKey("Bearer")
                    .Description("Paste your API token from Remote Control settings (Bearer prefix is added automatically)")
                    .Name("Authorization")
                    .In("header");

                var xmlPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(RemoteControlServer).Assembly.Location),
                    "SCRemoteControl.xml");
                if (System.IO.File.Exists(xmlPath))
                    c.IncludeXmlComments(xmlPath);
            });

            // Modern Swagger UI 5.x (served from embedded resources)
            app.Use<SwaggerUiMiddleware>();

            app.UseWebApi(config);
        }
    }
}
