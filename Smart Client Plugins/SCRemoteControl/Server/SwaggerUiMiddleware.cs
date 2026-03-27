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
    <title>SC Remote Control API</title>
    <link rel=""stylesheet"" href=""/swagger/swagger-ui.css"">
    <style>
        html { box-sizing: border-box; overflow-y: scroll; }
        *, *:before, *:after { box-sizing: inherit; }
        body { margin: 0; background: #fafafa; }
        .topbar { display: none; }
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
                description: 'Enter: Bearer {your-token}'
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
