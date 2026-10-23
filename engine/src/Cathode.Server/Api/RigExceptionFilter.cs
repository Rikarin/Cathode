using Cathode.Server.Rig;

namespace Cathode.Server.Api;

/// <summary>Turns <see cref="RigException" /> into an RFC 9457 problem response.</summary>
sealed class RigExceptionFilter : IEndpointFilter {
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
        try {
            return await next(context);
        } catch (RigException e) {
            var extensions = e.Errors.Count > 0 ? new Dictionary<string, object?> { ["errors"] = e.Errors } : null;
            return TypedResults.Problem(e.Message, statusCode: e.Status, extensions: extensions);
        }
    }
}
