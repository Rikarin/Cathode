using Cathode.Contracts;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>Failures the endpoints do not map themselves still answer an RFC 9457 problem, not an empty body.</summary>
public sealed class ErrorResponseTests {
    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_unexpected_failure_answers_a_problem_without_the_details() {
        await using var server = new ServerFixture();
        using var client = await server.ConnectedClientAsync();
        // The presets folder replaced by a file: saving a preset meets an IOException no endpoint maps.
        var presets = Path.Combine(server.DataDirectory, "presets");
        Directory.Delete(presets, true);
        await File.WriteAllTextAsync(presets, "not a folder", Token);

        using var response = await client.PostAsJsonAsync("/api/v1/live/save", new SaveRequest("P1", true), Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Equal(500, problem!.Status);
        Assert.DoesNotContain(server.DataDirectory, await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task A_looper_export_without_an_engine_is_unavailable() {
        // An engine socket nobody listens on: the Server runs, the engine never connects.
        var socket = Path.Combine(Path.GetTempPath(), $"cathode-nobody-{Guid.NewGuid():n}.sock");
        await using var server = new ServerFixture(socket);
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/v1/looper/export", Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Contains("not connected", problem!.Detail, StringComparison.Ordinal);
    }
}
