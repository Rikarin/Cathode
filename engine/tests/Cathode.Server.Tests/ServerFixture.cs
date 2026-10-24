using System.Net.Http.Json;
using Cathode.Contracts;
using Cathode.Dsp;
using Cathode.Engine.Graph;
using Cathode.EngineHost;
using Cathode.Server.Engine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace Cathode.Server.Tests;

/// <summary>
///     A Server with a throwaway data folder and, by default, an in-process engine (null audio). Pass a socket path
///     to talk to a separate cathode-host over IPC instead, and a token to require one on every request.
/// </summary>
public sealed class ServerFixture(string? socketPath = null, string? token = null) : WebApplicationFactory<Program> {
    readonly string dataDirectory = Directory.CreateTempSubdirectory("cathode-server-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseSetting("Cathode:DataDirectory", dataDirectory);
        builder.UseSetting("Cathode:Engine:Mode", socketPath is null ? "inprocess" : "ipc");
        if (token is not null) {
            builder.UseSetting("Cathode:Token", token);
        }

        if (socketPath is not null) {
            builder.UseSetting("Cathode:Engine:SocketPath", socketPath);
        }
    }

    /// <summary>The in-process engine host (in-process mode only).</summary>
    public EngineHostService Host =>
        ((InProcessEngineLink)Services.GetRequiredService<EngineConnection>().Link!).Service;

    public CompiledGraph? Graph =>
        (Services.GetRequiredService<EngineConnection>().Link as InProcessEngineLink)?.Service.Engine.Current;

    public async Task<HttpClient> ConnectedClientAsync() {
        var client = CreateClient();
        // Generous: a loaded CI runner can take a while to start a separate host process and connect to it.
        await Eventually(
            async () => (await client.GetFromJsonAsync<SystemInfo>("/api/v1/system"))!.Engine.Connected,
            30
        );
        return client;
    }

    public IDspBlock Block(string id) {
        var graph = Graph ?? throw new InvalidOperationException("No graph.");
        var slot = graph.SlotOf(id);
        return slot >= 0
            ? graph.Block(slot)
            : throw new InvalidOperationException($"No block '{id}' in the running graph (yet).");
    }

    public float Target(string block, string param) {
        var dsp = Block(block);
        return dsp.Params.GetTarget(dsp.Descriptor.IndexOf(param));
    }

    public bool IsBypassed(string block) => Graph!.IsBypassed(Graph.SlotOf(block));

    /// <summary>
    ///     Engine commands are asynchronous; waits up to <paramref name="seconds" /> for <paramref name="condition" />.
    ///     An exception counts as "not yet" (the graph may not have swapped in) until the deadline, then it is reported.
    /// </summary>
    public static async Task Eventually(Func<Task<bool>> condition, int seconds = 5) {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        Exception? last = null;
        while (DateTime.UtcNow < deadline) {
            try {
                if (await condition()) {
                    return;
                }
            } catch (Exception e) when (e is not XunitException) {
                last = e;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Condition not met within {seconds} s.{(last is null ? string.Empty : $" Last error: {last}")}");
    }

    public static Task Eventually(Func<bool> condition) => Eventually(() => Task.FromResult(condition()));

    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        if (disposing) {
            Directory.Delete(dataDirectory, true);
        }
    }
}
