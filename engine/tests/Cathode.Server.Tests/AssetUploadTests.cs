using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>Asset uploads at their limits and with hostile content.</summary>
public sealed class AssetUploadTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => client = await server.ConnectedClientAsync();

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Over_kestrel_uploads_up_to_64_mb_are_taken_and_larger_ones_are_refused_with_a_reason() {
        // A real Kestrel, whose default request limit (30,000,000 bytes) the in-memory test server does not have.
        await using var kestrel = new ServerFixture();
        kestrel.UseKestrel(0);
        kestrel.StartServer();
        using var http = kestrel.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(2);

        using var large = await http.PostAsync(
            "/api/v1/assets?name=big.nam",
            new ByteArrayContent(new byte[40 * 1024 * 1024]),
            Token
        );
        using var tooLarge = await http.PostAsync(
            "/api/v1/assets?name=huge.nam",
            new ByteArrayContent(new byte[64 * 1024 * 1024 + 1]),
            Token
        );

        Assert.Equal(HttpStatusCode.OK, large.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        var problem = await tooLarge.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Contains("64 MB", problem!.Detail, StringComparison.Ordinal);
    }
}
