using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Cathode.Server.Api;

/// <summary>
///     Who may call the API. With <see cref="ServerOptions.Token" /> set (the desktop app always sets one, and a Server
///     reachable beyond localhost must) every request needs <c>Authorization: Bearer &lt;token&gt;</c>, or
///     <c>?access_token=</c> on the WebSocket, which browsers cannot give headers; any origin may then call, as a page
///     cannot forge the token. Without a token (development) the Server listens on loopback only and answers only
///     this machine's own pages: a request that names another origin, or that was addressed to a host name other than
///     a loopback one, is refused before anything runs (<see cref="RefusalWithoutToken" />).
/// </summary>
public static class Access {
    public static void AddCathodeCors(this IServiceCollection services) => services.AddCors();

    public static void UseCathodeAccess(this WebApplication app, string? token) {
        if (token is null) {
            // Before CORS and everything else. CORS alone only keeps a foreign page from reading answers and from
            // sending requests that need a preflight; a body-less POST or a raw upload needs none and would run.
            app.Use(static async (context, next) => {
                    if (RefusalWithoutToken(context.Request) is { } reason) {
                        await TypedResults.Problem(reason, statusCode: StatusCodes.Status403Forbidden)
                            .ExecuteAsync(context);
                        return;
                    }

                    await next(context);
                }
            );
        }

        app.UseCors(policy => policy.SetIsOriginAllowed(origin => token is not null || IsLoopbackOrigin(origin))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("ETag")
        );

        if (token is null) {
            return;
        }

        var expected = Encoding.UTF8.GetBytes(token);
        app.Use(async (context, next) => {
                var presented = Presented(context.Request);
                // Out of the query once read, so nothing logged later (a request's URL in a log line) carries it.
                StripQueryToken(context.Request);
                if (HttpMethods.IsOptions(context.Request.Method)
                    || (presented is not null
                        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), expected))) {
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

    /// <summary>
    ///     Why a Server without a token refuses the request, or null when it may run. Three checks, each for what a
    ///     web page in a browser on this machine could otherwise do:
    ///     <list type="bullet">
    ///         <item>
    ///             the <c>Host</c> must be a loopback name or address: a page that rebinds its own name to 127.0.0.1
    ///             is same-origin with the Server as far as the browser knows, but still sends its own name here;
    ///         </item>
    ///         <item>
    ///             an <c>Origin</c>, when sent, must be a loopback one, on every path and method (the WebSocket
    ///             included, to which browsers apply no CORS);
    ///         </item>
    ///         <item>
    ///             a request that changes something and carries no <c>Origin</c> must not be marked
    ///             <c>Sec-Fetch-Site: cross-site</c> by the browser.
    ///         </item>
    ///     </list>
    ///     A client that is not a browser (curl, a script) sends neither header and is not affected.
    /// </summary>
    public static string? RefusalWithoutToken(HttpRequest request) {
        if (request.Host.Host is { Length: > 0 } host && !IsLoopbackHost(host)) {
            return $"This Server has no access token and answers only on localhost, not as '{host}'.";
        }

        if (request.Headers.Origin.ToString() is { Length: > 0 } origin) {
            return IsLoopbackOrigin(origin)
                ? null
                : $"This Server has no access token and takes no requests from '{origin}'.";
        }

        var reads = HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method);
        return !reads && request.Headers["Sec-Fetch-Site"].ToString() == "cross-site"
            ? "This Server has no access token and takes no requests from other sites."
            : null;
    }

    static string? Presented(HttpRequest request) {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
            return header[7..].Trim();
        }

        return request.Path.StartsWithSegments("/ws") ? request.Query["access_token"].ToString() : null;
    }

    static void StripQueryToken(HttpRequest request) {
        if (!request.Query.ContainsKey("access_token")) {
            return;
        }

        request.QueryString = QueryString.Create(
            request.Query.Where(static q => q.Key != "access_token")
                .SelectMany(static q => q.Value.Select(v => KeyValuePair.Create(q.Key, v)))
        );
    }

    static bool IsLoopbackOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && IsLoopbackHost(uri.Host);

    /// <summary><c>localhost</c> or a loopback address (an IPv6 one with or without its brackets).</summary>
    static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.AsSpan().Trim("[]"), out var address) && IPAddress.IsLoopback(address));
}
