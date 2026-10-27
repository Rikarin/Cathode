using Microsoft.Extensions.FileProviders;

namespace Cathode.Server.Api;

/// <summary>
///     Serves the editor's web build (<see cref="ServerOptions.EditorDirectory" />) at the root, so a headless rig is
///     played from any browser on the network: a phone, a tablet, a laptop. The files carry nothing secret, so they
///     need no token; every API call still does, and the page asks for it. <c>index.html</c> is marked same-origin, so
///     the editor talks to the Server that served it. Ahead of <see cref="Access.UseCathodeAccess" /> in the pipeline.
/// </summary>
public static class EditorHosting {
    const string Marker = """<meta name="cathode-server" content="same-origin">""";

    public static void UseCathodeEditor(this WebApplication app, string directory) {
        var root = Path.GetFullPath(directory);
        var indexPath = Path.Combine(root, "index.html");
        if (!File.Exists(indexPath)) {
            throw new InvalidOperationException($"No editor build in {root}: index.html is missing.");
        }

        var html = File.ReadAllText(indexPath);
        var index = html.Contains("</head>", StringComparison.Ordinal)
            ? html.Replace("</head>", Marker + "</head>", StringComparison.Ordinal)
            : Marker + html;
        app.Use(async (context, next) => {
                var path = context.Request.Path.Value;
                if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                    && path is "/" or "/index.html") {
                    // Never cached: a new build's hashed scripts must be picked up after an update.
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-cache";
                    await context.Response.WriteAsync(index);
                    return;
                }

                await next(context);
            }
        );
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(root) });
    }
}
