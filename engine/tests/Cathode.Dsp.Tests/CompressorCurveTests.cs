using System.Text.Json;
using System.Text.Json.Nodes;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     The Compressor's transfer curve (<see cref="CompressorCurve" />) and the <c>detector</c> and <c>output</c>
///     meters that place a signal on it. The editor draws the curve with a port of the same code; the cases here are
///     its fixtures, so the two cannot drift apart unseen.
/// </summary>
public class CompressorCurveTests {
    const double Rate = 48_000;

    /// <summary>The input levels each fixture case gives the output for, dB: the chart's range and beyond.</summary>
    static readonly float[] Levels = [..Enumerable.Range(0, 25).Select(static i => -66f + 3 * i)];

    /// <summary>The parameters the curve depends on: the fixtures give their defaults.</summary>
    static readonly string[] Shaping = [
        "type", "threshold", "ratio", "knee", "makeup", "autoMakeup", "inputLevel", "controls", "compression",
        "dynamics", "expThreshold", "expRatio"
    ];

    /// <summary>The settings the fixtures cover, by parameter id; the rest stay at their defaults.</summary>
    static readonly (string Name, Dictionary<string, float> Values)[] Cases = [
        ("pedal, the defaults", new() { ["type"] = CompressorBlock.Pedal }),
        ("studio, hard knee, makeup", new() {
            ["type"] = 1,
            ["threshold"] = -30,
            ["ratio"] = 2,
            ["knee"] = 0,
            ["makeup"] = 6
        }),
        ("studio, limiter, wide knee", new() { ["type"] = 1, ["threshold"] = -12, ["ratio"] = 100, ["knee"] = 12 }),
        ("opto, heavy", new() { ["type"] = CompressorBlock.Opto, ["threshold"] = -40, ["ratio"] = 10, ["knee"] = 3 }),
        ("sustainer", new() { ["type"] = CompressorBlock.Sustainer, ["threshold"] = -30, ["ratio"] = 4, ["knee"] = 6 }),
        ("dynamics compressing", new() { ["type"] = CompressorBlock.DynamicsProcessor, ["dynamics"] = 50 }),
        ("dynamics limiting", new() { ["type"] = CompressorBlock.DynamicsProcessor, ["dynamics"] = 100 }),
        ("dynamics expanding", new() { ["type"] = CompressorBlock.DynamicsProcessor, ["dynamics"] = -100 }),
        ("feedback", new() { ["type"] = CompressorBlock.Feedback, ["threshold"] = -30, ["ratio"] = 8 }),
        ("jfet, all buttons in", new() { ["type"] = CompressorBlock.Jfet, ["threshold"] = -24, ["ratio"] = 100 }),
        ("tube, its own knee",
            new() { ["type"] = CompressorBlock.Tube, ["threshold"] = -30, ["ratio"] = 6, ["knee"] = 0 }),
        ("compander", new() { ["type"] = CompressorBlock.Compander, ["expThreshold"] = -45, ["expRatio"] = 3 }),
        ("compression knob", new() { ["type"] = 0, ["controls"] = 1, ["compression"] = 70, ["threshold"] = 0 }),
        ("auto makeup", new() { ["type"] = 1, ["threshold"] = -30, ["ratio"] = 4, ["autoMakeup"] = 1 }),
        ("auto makeup, line level", new() { ["type"] = 0, ["ratio"] = 8, ["autoMakeup"] = 1, ["inputLevel"] = 1 })
    ];

    static string FixturePath =>
        Path.Combine(
            EngineRoot(),
            "..",
            "editor",
            "apps",
            "editor",
            "src",
            "app",
            "block-editor",
            "panels",
            "compressor-curve.fixtures.json"
        );

    [Theory]
    [InlineData(-40f, -40f)] // under the threshold: unity
    [InlineData(-10f, -17.5f)] // 10 dB over at 4:1
    [InlineData(0f, -15f)]
    public void The_curve_is_the_static_curve_on_a_feed_forward_type(float input, float expected) {
        var curve = Curve(new() { ["type"] = 0, ["threshold"] = -20, ["ratio"] = 4, ["knee"] = 0 });
        Assert.Equal(expected, curve.Output(input), 1e-4f);
    }

    [Fact]
    public void The_curve_follows_the_compression_knob_and_the_dynamics_and_tube_settings() {
        var knob = Curve(new() { ["controls"] = 1, ["compression"] = 100 });
        Assert.Equal((-50f, 20f), (knob.Threshold, knob.Ratio));
        Assert.Equal(CompressorBlock.InfiniteRatio, Curve(new() { ["type"] = 4, ["dynamics"] = 100 }).Ratio);
        Assert.Equal(CompressorBlock.TubeKneeDb, Curve(new() { ["type"] = 7, ["knee"] = 0 }).Knee);
        Assert.Equal(7.5f, Curve(new() { ["threshold"] = -20, ["knee"] = 0, ["autoMakeup"] = 1 }).Makeup, 1e-4f);
    }

    [Fact]
    public void A_feedback_types_curve_is_softer_than_its_ratio() {
        // The detector hears the output: over the threshold the output rises 1 / (2 - 1 / R) dB per dB, 2:1 at ∞:1.
        var curve = Curve(
            new() { ["type"] = CompressorBlock.Feedback, ["threshold"] = -30, ["ratio"] = 100, ["knee"] = 0 }
        );
        Assert.Equal(-15f, curve.Output(0f), 1e-3f);
        Assert.Equal(-40f, curve.Output(-40f), 1e-3f);
    }

    [Theory]
    [InlineData(CompressorBlock.Pedal, -10f, 0f, -10f)] // peak: the sine's peak
    [InlineData(CompressorBlock.Studio, -10f, 0f, -13.01f)] // RMS: 3 dB under the peak
    [InlineData(CompressorBlock.Pedal, -10f, 1f, -22f)] // Input Level Line takes 12 dB off
    public void The_detector_meter_reads_the_level_the_gain_computer_sees(
        int type,
        float peakDb,
        float line,
        float expected
    ) {
        using var block = Create(new() { ["type"] = type, ["inputLevel"] = line });
        var meters = new float[3];

        BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, Gain(peakDb)), 64);
        block.ReadMeters(meters);

        Assert.Equal(expected, meters[1], 0.1f);
    }

    [Fact]
    public void Silence_and_a_bypassed_block_rest_the_curve_meters_at_the_floor() {
        using var block = Create(new() { ["type"] = 0 });
        var meters = new float[3];

        BlockRender.Mono(block, new float[4_800], 64);
        block.ReadMeters(meters);
        Assert.Equal([0f, CompressorBlock.MeterFloorDb, CompressorBlock.MeterFloorDb], meters);

        meters = [9f, 9f, 9f];
        ((IBlockMeters)block).ReadRestingMeters(meters);
        Assert.Equal([0f, CompressorBlock.MeterFloorDb, CompressorBlock.MeterFloorDb], meters);
    }

    [Theory]
    [InlineData(-30f, 2f, 0f)]
    [InlineData(-30f, 4f, 0f)]
    [InlineData(-30f, 100f, 0f)]
    [InlineData(-20f, 4f, 12f)]
    [InlineData(-40f, 8f, 6f)]
    public void Steady_gain_reduction_on_a_sine_is_the_static_curves(float threshold, float ratio, float knee) {
        foreach (var peakDb in new[] { -36f, -24f, -12f, 0f }) {
            using var block = Create(
                new() {
                    ["type"] = CompressorBlock.Studio, ["threshold"] = threshold, ["ratio"] = ratio, ["knee"] = knee
                }
            );
            var meters = new float[3];
            BlockRender.Mono(block, Signals.Sine(1_000, Rate, 24_000, Gain(peakDb)), 64);
            block.ReadMeters(meters);

            var level = peakDb - 3.0103f; // the RMS detector reads a sine 3 dB under its peak
            var expected = -CompressorCurve.Of(block.Params).Target(level);
            Assert.True(
                Math.Abs(meters[0] - expected) < 0.25f,
                $"{peakDb} dBFS at {ratio}:1 over {threshold} dB: {meters[0]:0.00} dB of reduction, "
                + $"the curve {expected:0.00}"
            );
        }
    }

    [Theory]
    [InlineData(CompressorBlock.Pedal, -6f, 1.5f)]
    [InlineData(CompressorBlock.Studio, -6f, 0.3f)]
    [InlineData(CompressorBlock.Sustainer, -50f, 0.3f)]
    [InlineData(CompressorBlock.DynamicsProcessor, -6f, 0.3f)]
    [InlineData(CompressorBlock.Feedback, -6f, 0.5f)]
    [InlineData(CompressorBlock.Tube, -6f, 0.5f)]
    [InlineData(CompressorBlock.Compander, -70f, 0.5f)]
    public void A_steady_signal_reads_a_point_on_the_curve(int type, float peakDb, float tolerance) {
        using var block = Create(
            new() {
                ["type"] = type,
                ["threshold"] = -30,
                ["ratio"] = 4,
                ["makeup"] = 6,
                ["dynamics"] = type == CompressorBlock.DynamicsProcessor ? -50 : 50
            }
        );
        var meters = new float[3];

        BlockRender.Mono(block, Signals.Sine(1_000, Rate, 48_000, Gain(peakDb)), 64);
        block.ReadMeters(meters);

        var curve = CompressorCurve.Of(block.Params);
        Assert.True(
            Math.Abs(meters[2] - curve.Output(meters[1])) < tolerance,
            $"read ({meters[1]:0.00}, {meters[2]:0.00}) dB, the curve gives {curve.Output(meters[1]):0.00} dB there"
        );
    }

    /// <summary>
    ///     The editor's port of the curve is pinned to these values (compressor-curve.spec.ts). When the gain computer
    ///     changes on purpose, port the change, then update them with <c>CATHODE_UPDATE_CURVES=1 dotnet test</c> (and
    ///     <c>pnpm prettier --write</c> the file; this compares values, not text).
    /// </summary>
    [Fact]
    public void The_editors_curve_fixtures_match_the_engine() {
        var current = Fixtures();
        if (Environment.GetEnvironmentVariable("CATHODE_UPDATE_CURVES") == "1") {
            File.WriteAllText(FixturePath, current.ToJsonString(new() { WriteIndented = true }) + "\n");
        }

        var committed = JsonNode.Parse(File.ReadAllText(FixturePath))!;
        Assert.Equal(Levels, committed["levels"]!.AsArray().Select(static l => l!.GetValue<float>()));
        Assert.Equal(Defaults(), committed["defaults"].Deserialize<Dictionary<string, float>>());
        var cases = committed["cases"]!.AsArray();
        Assert.Equal(Cases.Length, cases.Count);
        for (var c = 0; c < Cases.Length; c++) {
            var (name, values) = Cases[c];
            var stored = cases[c]!;
            Assert.Equal(name, stored["name"]!.GetValue<string>());
            Assert.Equal(values, stored["values"].Deserialize<Dictionary<string, float>>());
            var output = stored["output"]!.AsArray().Select(static o => o!.GetValue<float>()).ToArray();
            var curve = Curve(values);
            for (var i = 0; i < Levels.Length; i++) {
                Assert.True(
                    Math.Abs(output[i] - curve.Output(Levels[i])) < 1e-3f,
                    $"{name} at {Levels[i]} dB: the fixture says {output[i]}, the engine {curve.Output(Levels[i])}. "
                    + "Port the change to compressor-curve.ts, then update the fixtures with CATHODE_UPDATE_CURVES=1."
                );
            }
        }
    }

    static JsonObject Fixtures() =>
        new() {
            ["levels"] = new JsonArray([..Levels.Select(static l => JsonValue.Create(l))]),
            ["defaults"] = JsonSerializer.SerializeToNode(Defaults()),
            ["cases"] = new JsonArray(
                [
                    ..Cases.Select(static c => new JsonObject {
                            ["name"] = c.Name,
                            ["values"] = JsonSerializer.SerializeToNode(c.Values),
                            ["output"] = new JsonArray(
                                [..Levels.Select(l => JsonValue.Create(MathF.Round(Curve(c.Values).Output(l), 4)))]
                            )
                        }
                    )
                ]
            )
        };

    static Dictionary<string, float> Defaults() =>
        Shaping.ToDictionary(
            static id => id,
            static id => CompressorBlock.Info.Params[CompressorBlock.Info.IndexOf(id)].Default
        );

    static float Gain(float db) => MathF.Pow(10f, db / 20f);

    static CompressorCurve Curve(Dictionary<string, float> values) {
        using var block = Create(values);
        return CompressorCurve.Of(block.Params);
    }

    static CompressorBlock Create(Dictionary<string, float> values) {
        var block = new CompressorBlock();
        foreach (var (id, value) in values) {
            block.Params.SetTarget(CompressorBlock.Info.IndexOf(id), value);
        }

        block.Prepare(new(Rate, 64));
        return block;
    }

    static string EngineRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Cathode.slnx"))) {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the engine folder.");
    }
}
