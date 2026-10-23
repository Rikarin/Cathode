using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Cathode.Server.Api;

/// <summary>
///     Makes the OpenAPI document say what the Server actually sends: every property that is not nullable is always
///     present, so it is required; nullable ones are left out when null (<c>WhenWritingNull</c>), so they are not.
///     Generated clients (editor/libs/api-client) then get exact types instead of everything-optional ones.
/// </summary>
static class OpenApiSchemas {
    public static void MarkNonNullablePropertiesRequired(this OpenApiOptions options) =>
        options.AddSchemaTransformer((schema, _, _) => {
                if (schema.Properties is { Count: > 0 } properties) {
                    foreach (var (name, property) in properties) {
                        if (property is OpenApiSchemaReference
                            || property.Type is { } type && !type.HasFlag(JsonSchemaType.Null)) {
                            (schema.Required ??= new HashSet<string>()).Add(name);
                        } else {
                            schema.Required?.Remove(name);
                        }
                    }
                }

                return Task.CompletedTask;
            }
        );
}
