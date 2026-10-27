using Cathode.Contracts;
using Cathode.Server.Midi;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>The MIDI output setting: where scene MIDI and footswitch LEDs go (GlobalSettings.MidiOutPort).</summary>
public sealed class MidiOutputTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    readonly List<(string? Port, byte[] Bytes)> sent = [];
    HttpClient client = null!;

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        server.Host.NativeMidiSend = (port, bytes) => {
            lock (sent) {
                sent.Add((port, bytes));
            }

            return true;
        };
        server.Host.NativeMidiPorts = static () => [
            new("in-1", "Pedalboard In", true),
            new("out-1", "Pedalboard Out", false),
            new("out-2", "Synth", false)
        ];
        server.Services.GetRequiredService<MidiRouter>();
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task The_outputs_are_the_hosts_midi_outputs() {
        var outputs = await client.GetFromJsonAsync<MidiOutputInfo[]>("/api/v1/midi/outputs", Token);

        Assert.Equal([new("out-1", "Pedalboard Out"), new("out-2", "Synth")], outputs!);
    }

    [Fact]
    public async Task Scene_midi_and_footswitch_leds_go_to_the_chosen_output() {
        await PutGlobal(static g => {
                g.MidiOutPort = "out-2";
                g.Footswitches.Add(new("board", "Board", [new(1, "Scene 2", "cc", 1, 33, new("scene", Value: 1))]));
                g.ActiveLayout = "board";
            }
        );
        await Send(
            client.PatchAsJsonAsync("/api/v1/live/scenes/1", new SceneUpdateRequest(Midi: [new("pc", 1, 9)]), Token)
        );
        await Send(client.PutAsJsonAsync("/api/v1/live/scenes/active", new SceneSelectRequest(1), Token));

        Assert.Equal("out-2", (await Global()).MidiOutPort);
        await ServerFixture.Eventually(() => Sent("out-2", [0xC0, 9]) && Sent("out-2", [0xB0, 33, 127]));
        lock (sent) {
            Assert.All(sent, static m => Assert.Equal("out-2", m.Port));
        }

        // Back to every output (blank is none): the LEDs are sent again, to all of them.
        await PutGlobal(static g => g.MidiOutPort = " ");
        Assert.Null((await Global()).MidiOutPort);
        await ServerFixture.Eventually(() => Sent(null, [0xB0, 33, 127]));
    }

    bool Sent(string? port, byte[] message) {
        lock (sent) {
            return sent.Any(m => m.Port == port && m.Bytes.SequenceEqual(message));
        }
    }

    async Task<GlobalSettings> Global() => (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;

    async Task PutGlobal(Action<GlobalSettings> change) {
        var global = await Global();
        change(global);
        await Send(client.PutAsJsonAsync("/api/v1/global", global, Token));
    }

    static async Task Send(Task<HttpResponseMessage> call) {
        using var response = await call;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
    }
}
