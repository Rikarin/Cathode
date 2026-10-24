using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Cathode.Server.Api;

/// <summary>
///     Who may call the API. With <see cref="ServerOptions.Token" /> set (the desktop app always sets one, and a Server
///     reachable beyond localhost must) every request needs <c>Authorization: Bearer &lt;token&gt;</c>, or
///     <c>?access_token=</c> on the WebSocket, which browsers cannot give headers; any origin may then call, as a page
///     cannot forge the token. Without a token (development) only loopback origins pass CORS, so an arbitrary web page
///     cannot drive the rig through the browser.
/// </summary>
public static class Access {
    public static void AddCathodeCors(this IServiceCollection services) => services.AddCors();

    public static void UseCathodeAccess(this WebApplication app, string? token) {
        app.UseCors(policy => policy.SetIsOriginAllowed(origin => token is not null || IsLoopbackOrigin(origin))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("ETag")
        );

        if (token is null) {
            // Browsers apply no CORS to WebSockets, so check the origin here: no foreign page may listen in.
            app.Use(async (context, next) => {
                    if (context.Request.Path.StartsWithSegments("/ws")
                        && context.Request.Headers.Origin.ToString() is { Length: > 0 } origin
                        && !IsLoopbackOrigin(origin)) {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }

                    await next(context);
                }
            );
            return;
        }

        var expected = Encoding.UTF8.GetBytes(token);
        app.Use(async (context, next) => {
                if (HttpMethods.IsOptions(context.Request.Method)
                    || Presented(context.Request) is { } presented
                    && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), expected)) {
                    await next(context);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
            }
        );
    }

    /// <summary>Refuses to expose an unauthenticated Server beyond this machine.</summary>
    public static void RequireTokenBeyondLoopback(string urls, string? token) {
        if (token is not null) {
            return;
        }

        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var host = new Uri(
                url.Replace("*", "0.0.0.0", StringComparison.Ordinal)
                    .Replace(
                        "+",
                        "0.0.0.0",
                        StringComparison.Ordinal
                    )
            ).Host;
            if (host != "localhost" && !(IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))) {
                throw new InvalidOperationException(
                    $"Listening on {url} needs an access token: set Cathode:Token (or the CATHODE__TOKEN variable)."
                );
            }
        }
    }

    static string? Presented(HttpRequest request) {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
            return header[7..].Trim();
        }

        return request.Path.StartsWithSegments("/ws") ? request.Query["access_token"].ToString() : null;
    }

    static bool IsLoopbackOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Host == "localhost" || IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
}
