using Cathode.Contracts;
using Cathode.Ipc;
using Cathode.Server.Engine;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>The Server's engine link against a fake host that misbehaves as a crashing cathode-host does.</summary>
public sealed class EngineLinkTests : IAsyncDisposable {
    readonly FakeHost host = new(
        Path.Combine(Path.GetTempPath(), $"cth-{Guid.NewGuid().ToString("n")[..8]}.sock")
    ); // Unix socket paths are limited to ~104 bytes on macOS

    CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => host.DisposeAsync();

    [Fact]
    public async Task A_host_that_dies_mid_request_neither_wedges_the_rig_nor_the_link() {
        await using var server = new ServerFixture(host.SocketPath);
        using var client = await server.ConnectedClientAsync();

        // The host dies while it compiles the graph of a structural edit, which holds the rig's lock.
        host.DieOn = static m => m.Type == IpcTypes.LoadGraph;
        using var added = await client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("delay", 0, 2), Token)
            .WaitAsync(TimeSpan.FromSeconds(20), Token);

        // The edit stands, as it would with no engine connected: the resync after the reconnection sends it.
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync(Token));
        var state = await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token)
            .WaitAsync(TimeSpan.FromSeconds(20), Token);
        Assert.Contains(state!.Preset.Blocks, static b => b.Id == "delay1");

        await ServerFixture.Eventually(async () => host.Connections >= 2
            && (await client.GetFromJsonAsync<SystemInfo>("/api/v1/system", Token))!.Engine.Connected
        );
        var resent = host.Received.LastOrDefault(static m => m.Type == IpcTypes.LoadGraph);
        Assert.Contains(resent!.Graph!.Nodes, static n => n.Id == "delay1");

        // Later edits reach the new link.
        using var removed = await client.DeleteAsync("/api/v1/live/blocks/delay1", Token)
            .WaitAsync(TimeSpan.FromSeconds(20), Token);
        Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task A_failing_midi_handler_leaves_the_engine_link_up() {
        await using var server = new ServerFixture(host.SocketPath);
        using var client = await server.ConnectedClientAsync();
        var connection = server.Services.GetRequiredService<EngineConnection>();
        // A listener that fails the way MIDI learn on an unwritable data folder does.
        connection.MidiReceived += static _ => throw new IOException("The disk is full.");

        await Ok(client.PutAsJsonAsync("/api/v1/midi/learn", new MidiLearnRequest("tuner"), Token));
        await host.PushAsync(new() { Type = IpcTypes.Midi, Midi = [new(0xB0, 64, 127, 0)] });

        // The other listeners (the MIDI router) still ran, and the link stayed up: no reconnect, no resync.
        await ServerFixture.Eventually(async () =>
            (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!.MidiMappings.Exists(static m =>
                m is { Action: "tuner", Number: 64 }
            )
        );
        await Task.Delay(300, Token);
        Assert.Equal(1, host.Connections);
        Assert.True((await client.GetFromJsonAsync<SystemInfo>("/api/v1/system", Token))!.Engine.Connected);
        Assert.DoesNotContain("Engine link lost", server.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_ipc_client_survives_a_handler_that_throws() {
        await using var client = new IpcEngineClient(host.SocketPath);
        var disconnected = false;
        client.Disconnected += () => disconnected = true;
        client.MidiReceived += static _ => throw new IOException("The disk is full.");
        client.TelemetryReceived += static _ => throw new InvalidOperationException("Broken listener.");
        await client.ConnectAsync(TimeSpan.FromSeconds(10), Token);
        await client.SendAsync(new() { Type = IpcTypes.Hello }, Token);

        await host.PushAsync(new() { Type = IpcTypes.Midi, Midi = [new(0xB0, 1, 2, 0)] });
        await host.PushAsync(new() { Type = IpcTypes.Telemetry, Telemetry = new() });
        var reply = await client.SendAsync(new() { Type = IpcTypes.Hello }, Token);

        Assert.Equal("fake", reply.Host!.Backend);
        Assert.False(disconnected);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task The_tuner_and_midi_clock_are_resent_after_the_host_restarts() {
        await using var server = new ServerFixture(host.SocketPath);
        using var client = await server.ConnectedClientAsync();
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.TempoMode = "midiClock";
        await Ok(client.PutAsJsonAsync("/api/v1/global", global, Token));
        await Ok(client.PutAsJsonAsync("/api/v1/tuner", new BypassRequest(false), Token));

        var before = host.Received.Count;
        host.Disconnect(); // the desktop restarts cathode-host on every audio-settings change
        await ServerFixture.Eventually(async () => host.Connections >= 2
            && (await client.GetFromJsonAsync<SystemInfo>("/api/v1/system", Token))!.Engine.Connected
        );

        // The resync queues its commands; Connected only says the handlers ran, not that the host has read them yet.
        List<IpcMessage> Resync() => host.Received.Skip(before).ToList();
        await ServerFixture.Eventually(
            () => Resync().Exists(static m => m is { Type: IpcTypes.SetMidi, FollowClock: true })
                && Resync().Exists(static m => m is { Type: IpcTypes.SetTuner, Enabled: true }),
            () => $"the host received {string.Join(", ", Resync().Select(static m => m.Type))}"
        );
    }

    static async Task Ok(Task<HttpResponseMessage> request) {
        using var response = await request;
        Assert.True(
            response.IsSuccessStatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }
}
