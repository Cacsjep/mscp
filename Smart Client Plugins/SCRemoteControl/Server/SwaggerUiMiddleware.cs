using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;

namespace SCRemoteControl.Server
{
    using AppFunc = Func<IDictionary<string, object>, Task>;

    /// <summary>
    /// OWIN middleware that serves modern Swagger UI 5.x from embedded resources,
    /// pointing at the Swashbuckle-generated spec at /swagger/docs/v1.
    /// </summary>
    class SwaggerUiMiddleware
    {
        private readonly AppFunc _next;
        private byte[] _cachedHtml;
        private byte[] _cachedBundleJs;
        private byte[] _cachedCss;

        public SwaggerUiMiddleware(AppFunc next)
        {
            _next = next;
        }

        public Task Invoke(IDictionary<string, object> environment)
        {
            var context = new OwinContext(environment);
            var path = context.Request.Path.Value?.TrimEnd('/') ?? "";

            if (path.Equals("/swagger/swagger-ui-bundle.js", StringComparison.OrdinalIgnoreCase))
            {
                if (_cachedBundleJs == null)
                    _cachedBundleJs = LoadResource("SCRemoteControl.OpenApi.swagger_ui.swagger-ui-bundle.js");
                return Serve(context, _cachedBundleJs, "application/javascript");
            }

            if (path.Equals("/swagger/swagger-ui.css", StringComparison.OrdinalIgnoreCase))
            {
                if (_cachedCss == null)
                    _cachedCss = LoadResource("SCRemoteControl.OpenApi.swagger_ui.swagger-ui.css");
                return Serve(context, _cachedCss, "text/css");
            }

            if (path.Equals("", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/swagger");
                return Task.CompletedTask;
            }

            // Any other /swagger/* path (including old /swagger/ui/index) -> serve modern UI
            if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/swagger/docs", StringComparison.OrdinalIgnoreCase))
            {
                if (_cachedHtml == null)
                    _cachedHtml = BuildHtml();
                return Serve(context, _cachedHtml, "text/html; charset=utf-8");
            }

            return _next(environment);
        }

        private static Task Serve(OwinContext context, byte[] data, string contentType)
        {
            if (data == null)
            {
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            }
            context.Response.ContentType = contentType;
            context.Response.Headers["Cache-Control"] = "public, max-age=86400";
            context.Response.ContentLength = data.Length;
            return context.Response.WriteAsync(data);
        }

        private static byte[] BuildHtml()
        {
            return Encoding.UTF8.GetBytes(@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Remote Control API</title>
    <link rel=""stylesheet"" href=""/swagger/swagger-ui.css"">
    <style>
        html { box-sizing: border-box; overflow-y: scroll; }
        *, *:before, *:after { box-sizing: inherit; }
        body { margin: 0; background: #1a1a2e; color: #e0e0e0; }
        .topbar { display: none; }
        /* Dark theme overrides */
        .swagger-ui { color: #e0e0e0; }
        .swagger-ui .info .title, .swagger-ui .info .title small { color: #e0e0e0; }
        .swagger-ui .info p, .swagger-ui .info li { color: #b0b0b0; }
        .swagger-ui .scheme-container { background: #16213e; box-shadow: none; }
        .swagger-ui .opblock-tag { color: #e0e0e0 !important; border-bottom-color: #2a2a4a !important; }
        .swagger-ui .opblock-tag:hover { background: #16213e; }
        .swagger-ui .opblock { background: #16213e; border-color: #2a2a4a; }
        .swagger-ui .opblock .opblock-summary { border-color: #2a2a4a; }
        .swagger-ui .opblock .opblock-summary-description { color: #b0b0b0; }
        .swagger-ui .opblock .opblock-section-header { background: #0f1a33; }
        .swagger-ui .opblock .opblock-section-header h4 { color: #e0e0e0; }
        .swagger-ui .opblock-body pre { background: #0a0f1e; color: #e0e0e0; }
        .swagger-ui .opblock-body pre span { color: #e0e0e0 !important; }
        .swagger-ui .opblock.opblock-get { background: rgba(61,174,233,.1); border-color: #3daee9; }
        .swagger-ui .opblock.opblock-get .opblock-summary { border-color: #3daee9; }
        .swagger-ui .opblock.opblock-post { background: rgba(73,204,144,.1); border-color: #49cc91; }
        .swagger-ui .opblock.opblock-post .opblock-summary { border-color: #49cc91; }
        .swagger-ui table thead tr th, .swagger-ui table thead tr td { color: #e0e0e0; border-bottom-color: #2a2a4a; }
        .swagger-ui table tbody tr td { color: #b0b0b0; border-bottom-color: #2a2a4a; }
        .swagger-ui .parameter__name { color: #e0e0e0; }
        .swagger-ui .parameter__type { color: #8899aa; }
        .swagger-ui input[type=text], .swagger-ui textarea, .swagger-ui select { background: #0a0f1e; color: #e0e0e0; border-color: #2a2a4a; }
        .swagger-ui .model-box { background: #0a0f1e; }
        .swagger-ui .model { color: #e0e0e0; }
        .swagger-ui .model-title { color: #e0e0e0; }
        .swagger-ui .prop-type { color: #3daee9; }
        .swagger-ui .response-col_status { color: #e0e0e0; }
        .swagger-ui .response-col_description { color: #b0b0b0; }
        .swagger-ui .responses-inner h4, .swagger-ui .responses-inner h5 { color: #e0e0e0; }
        .swagger-ui .btn { background: #16213e; color: #e0e0e0; border-color: #2a2a4a; }
        .swagger-ui .btn:hover { background: #1a2745; }
        .swagger-ui .btn.authorize { background: #49cc91; color: #1a1a2e; border-color: #49cc91; }
        .swagger-ui .btn.authorize svg { fill: #1a1a2e; }
        .swagger-ui .authorization__btn svg { fill: #e0e0e0; }
        .swagger-ui .dialog-ux .modal-ux { background: #16213e; border-color: #2a2a4a; }
        .swagger-ui .dialog-ux .modal-ux-header h3 { color: #e0e0e0; }
        .swagger-ui .dialog-ux .modal-ux-content p { color: #b0b0b0; }
        .swagger-ui .auth-wrapper input { background: #0a0f1e; color: #e0e0e0; border-color: #2a2a4a; }
        .swagger-ui .loading-container .loading:after { color: #e0e0e0; }
        .swagger-ui .markdown p, .swagger-ui .markdown li { color: #b0b0b0; }
        .swagger-ui .model-box-control, .swagger-ui .models-control { color: #e0e0e0 !important; }
    </style>
</head>
<body>
    <div id=""swagger-ui""></div>
    <script src=""/swagger/swagger-ui-bundle.js""></script>
    <script>
        // Fetch spec and inject security definitions for modern Swagger UI
        fetch('/swagger/docs/v1').then(r => r.json()).then(spec => {
            spec.securityDefinitions = spec.securityDefinitions || {};
            spec.securityDefinitions.Bearer = {
                type: 'apiKey',
                name: 'Authorization',
                in: 'header',
                description: 'Paste your API token (Bearer prefix is added automatically)'
            };
            spec.security = [{ Bearer: [] }];

            SwaggerUIBundle({
                spec: spec,
                dom_id: '#swagger-ui',
                deepLinking: true,
                presets: [
                    SwaggerUIBundle.presets.apis,
                    SwaggerUIBundle.SwaggerUIStandalonePreset
                ],
                layout: 'BaseLayout',
                persistAuthorization: true,
                requestInterceptor: function(req) {
                    var auth = req.headers['Authorization'];
                    if (auth && !auth.toLowerCase().startsWith('bearer ')) {
                        req.headers['Authorization'] = 'Bearer ' + auth;
                    }
                    return req;
                }
            });
        });
    </script>
</body>
</html>");
        }

        private static byte[] LoadResource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    SCRemoteControlDefinition.Log.Error($"Embedded resource not found: {name}");
                    return null;
                }
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
    }
}
