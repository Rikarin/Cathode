using Cathode.Contracts;
using Cathode.Dsp.IO;
using Cathode.Testing;
using System.Net;
using System.Net.Http.Json;

namespace Cathode.Server.Tests;

/// <summary>Built-in reamp: <c>POST /live/render</c> plays a recording through the working copy offline.</summary>
public sealed class ReampApiTests : IAsyncLifetime {
    readonly ServerFixture server = new();
    HttpClient client = null!;

    CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() {
        client = await server.ConnectedClientAsync();
        await Ensure(client.PostAsJsonAsync("/api/v1/live/new", new { }, Token));
    }

    public async ValueTask DisposeAsync() {
        client.Dispose();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task A_recording_comes_back_through_the_working_copy_at_its_own_rate_and_length() {
        // 44.1 kHz and mono, where the engine that plays runs at 48 kHz: the render has an engine of its own.
        var di = Signals.Concat(new float[1_000], Signals.Sine(441, 44_100, 20_000, 0.25f));

        var unity = await Render(di, 44_100);
        await Ensure(client.PatchAsJsonAsync("/api/v1/live/scenes/0", new SceneUpdateRequest(Level: -6), Token));
        var quieter = await Render(di, 44_100);

        Assert.Equal((44_100, 2, di.Length), (unity.SampleRate, unity.Channels.Length, unity.Frames));
        Assert.Equal(unity.Channels[0], unity.Channels[1]);
        // An empty preset passes Input 1 to Output 1: the render is the recording, frame for frame.
        Assert.InRange(Residual(unity.Channels[0], di, 1), 0, 1e-3);
        // The unsaved scene level is in the render.
        Assert.InRange(Residual(quieter.Channels[0], di, MathF.Pow(10, -6 / 20f)), 0, 1e-3);
    }

    [Fact]
    public async Task The_render_stays_in_time_with_the_recording_whatever_the_graphs_latency() {
        await Ensure(client.PostAsJsonAsync("/api/v1/live/blocks", new AddBlockRequest("compressor", 2, 3), Token));
        await Ensure(
            client.PatchAsJsonAsync(
                "/api/v1/live/blocks/compressor1/params",
                new Dictionary<string, float> { ["lookAhead"] = 5 }, // 5 ms: 240 frames of latency
                Token
            )
        );
        await ServerFixture.Eventually(async () =>
            (await client.GetFromJsonAsync<LatencyInfo>("/api/v1/system/latency", Token))!.GraphFrames > 0
        );
        const int at = 4_800;

        var rendered = await Render(Signals.Impulse(9_600, 0.5f, at), 48_000);

        var left = rendered.Channels[0];
        var peak = Array.IndexOf(left, left.MaxBy(MathF.Abs));
        Assert.Equal(9_600, rendered.Frames);
        Assert.InRange(peak, at - 1, at + 1);
    }

    [Fact]
    public async Task A_tail_is_added_after_the_recording() {
        var di = Signals.Sine(440, 48_000, 4_800, 0.25f);

        var rendered = await Render(di, 48_000, "?tail=0.5");

        Assert.Equal(4_800 + 24_000, rendered.Frames);
        Assert.All(rendered.Channels[0][^1_000..], static v => Assert.Equal(0, v, 1e-6f));
    }

    [Fact]
    public async Task The_rigs_mute_and_the_metronome_are_left_out_of_the_render() {
        var global = (await client.GetFromJsonAsync<GlobalSettings>("/api/v1/global", Token))!;
        global.Metronome = true;
        global.MetronomeLevel = 0;
        await Ensure(client.PutAsJsonAsync("/api/v1/global", global, Token));
        await Ensure(client.PutAsJsonAsync("/api/v1/live/mute", new MuteRequest(true), Token));
        var di = Signals.Sine(440, 48_000, 48_000, 0.25f);

        var rendered = await Render(di, 48_000);

        Assert.InRange(Residual(rendered.Channels[0], di, 1), 0, 1e-3); // not muted, and no click on top
        Assert.True((await client.GetFromJsonAsync<LiveState>("/api/v1/live", Token))!.Muted); // the rig still is
    }

    [Fact]
    public async Task What_is_not_a_recording_the_engine_can_play_is_refused() {
        var garbage = await Post([.."definitely not a WAV file"u8], string.Empty);
        var empty = await Post(Wav([], 48_000), string.Empty);
        var telephone = await Post(Wav(Signals.Sine(440, 8_000, 800), 8_000), string.Empty);
        var endless = await Post(Wav(Signals.Sine(440, 48_000, 480), 48_000), "?tail=3600");

        Assert.Equal(HttpStatusCode.BadRequest, garbage.Status);
        Assert.Contains("not a WAV file", garbage.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        Assert.Equal(HttpStatusCode.BadRequest, telephone.Status);
        Assert.Contains("8000 Hz", telephone.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, endless.Status);
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "cathode-render-*-in.wav"));
    }

    /// <summary>The largest difference between <paramref name="rendered" /> and the recording at a gain.</summary>
    static float Residual(float[] rendered, float[] recording, float gain) =>
        rendered.Zip(recording, (a, b) => MathF.Abs(a - gain * b)).Max();

    async Task<AudioData> Render(float[] recording, int sampleRate, string query = "") {
        using var response = await client.PostAsync(
            "/api/v1/live/render" + query,
            new ByteArrayContent(Wav(recording, sampleRate)),
            Token
        );
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("audio/wav", response.Content.Headers.ContentType?.MediaType);
        return WavFile.Read(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Token)));
    }

    async Task<(HttpStatusCode Status, string Body)> Post(byte[] body, string query) {
        using var response = await client.PostAsync("/api/v1/live/render" + query, new ByteArrayContent(body), Token);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Token));
    }

    internal static byte[] Wav(float[] samples, int sampleRate) {
        using var stream = new MemoryStream();
        WavFile.Write(stream, new(sampleRate, [samples]), WavSampleFormat.Pcm24);
        return stream.ToArray();
    }

    async Task Ensure(Task<HttpResponseMessage> request) {
        using var response = await request;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
    }
}
