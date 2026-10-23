using System.Diagnostics;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

public class DriveCatalogueTests(ITestOutputHelper output) {
    const int Length = 65_536;
    const int FundamentalBin = 3_413; // prime, about 2.5 kHz at 48 kHz

    public static TheoryData<int> CircuitTypes =>
        [..Enumerable.Range(DriveBlock.CircuitTypes, DriveCircuits.All.Count)];

    public static TheoryData<int> AllTypes => [..Enumerable.Range(0, DriveBlock.CircuitTypes + DriveCircuits.All.Count)];

    [Fact]
    public void Original_types_keep_their_indices_and_the_catalogue_follows() {
        var options = DriveBlock.Info.Params[DriveBlock.Type].Options!;

        Assert.Equal(["Overdrive", "Distortion", "Fuzz"], options.Take(DriveBlock.CircuitTypes));
        Assert.Equal(DriveCircuits.All.Select(static c => c.Name), options.Skip(DriveBlock.CircuitTypes));
        Assert.True(DriveCircuits.All.Count >= 20, $"only {DriveCircuits.All.Count} circuits");
        Assert.Equal(options.Count, options.Distinct().Count());
        Assert.Equal(["type", "drive", "tone", "level", "mix"], DriveBlock.Info.Params.Select(static p => p.Id));
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Latency_is_the_same_for_every_type(int type) {
        using var block = Create(type, 5f);
        using var reference = Create(0, 5f);
        Assert.Equal(reference.LatencySamples, block.LatencySamples);
    }

    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Gain_is_sensible_at_low_and_full_drive(int type) {
        var input = Signals.CoherentSine(85, 8_192, 0.1f); // about 500 Hz, a firmly picked note

        (double GainDb, double Thd) Measure(float drive) {
            using var block = Create(type, drive, 5f);
            var rendered = BlockRender.Mono(block, Signals.Concat(input, input), 64).AsSpan(8_192);
            Assert.True(Analysis.AllFinite(rendered));
            return (Analysis.ToDb(Analysis.Rms(rendered) / Analysis.Rms(input)), Analysis.Thd(rendered, 85));
        }

        var low = Measure(0f);
        var full = Measure(10f);
        output.WriteLine(
            $"{Name(type)}: {low.GainDb:0.0} dB / THD {low.Thd:P1} at drive 0, {full.GainDb:0.0} dB / THD {full.Thd:P1} at 10"
        );
        Assert.InRange(low.GainDb, -20, 20);
        Assert.InRange(full.GainDb, -12, 24);
        Assert.True(full.Thd > 0.01, "full drive barely distorts");
        if (DriveCircuits.All[type - DriveBlock.CircuitTypes].Stages.All(static s => s.Kind != StageKind.Rectifier)) {
            Assert.True(full.Thd > low.Thd, "full drive is not more distorted"); // an octave is all "distortion"
        }
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Every_type_stays_finite_and_bounded_under_harsh_input(int type) {
        var square = Signals.Sine(997, 48_000, 9_600).Select(static s => s >= 0 ? 1f : -1f).ToArray();
        var stimuli = new[] { Signals.Noise(9_600, 5), square, Signals.Step(9_600) };
        foreach (var drive in new[] { 0f, 10f }) {
            foreach (var tone in new[] { 0f, 10f }) {
                using var block = Create(type, drive, tone);
                foreach (var stimulus in stimuli) {
                    var rendered = BlockRender.Mono(block, Signals.Concat(stimulus, new float[24_000]), 64);
                    Assert.True(Analysis.AllFinite(rendered), $"non-finite at drive {drive}, tone {tone}");
                    Assert.True(Analysis.ToDb(Analysis.Peak(rendered)) < 30, $"peak at drive {drive}, tone {tone}");
                    // The original voicings can still emit a few subnormals after a DC step; the circuits flush them.
                    if (type >= DriveBlock.CircuitTypes) {
                        Assert.False(Analysis.AnySubnormal(rendered), "subnormal output");
                    }

                    Assert.True(Analysis.ToDb(Analysis.Peak(rendered.AsSpan(rendered.Length - 4_800))) < -100, "no decay");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Aliasing_stays_60_db_down_at_full_drive(int type) {
        var input = Signals.CoherentSine(FundamentalBin * 2, Length * 2, 0.5f);

        double Measure(bool antialias) {
            using var block = Create(type, 10f, antialias: antialias);
            return Analysis.SignalToAliasingDb(BlockRender.Mono(block, input, 64).AsSpan(Length), FundamentalBin);
        }

        var exact = Measure(false);
        var aliasing = Measure(true);
        output.WriteLine($"{Name(type)}: signal-to-aliasing {exact:0.0} dB exact solve, {aliasing:0.0} dB antialiased");
        Assert.True(aliasing >= 60, $"signal-to-aliasing {aliasing:0.0} dB (target 60 dB, as the original voicings)");
    }

    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Partial_mix_does_not_comb(int type) {
        using var block = Create(type, 0f);
        block.Params.SetTarget(DriveBlock.Mix, 50f);
        block.Reset();
        var impulse = BlockRender.Mono(block, Signals.Impulse(8_192, 0.01f), 64);

        foreach (var f in new[] { 200.0, 1_000, 3_000, 6_000 }) {
            Assert.True(Analysis.ToDb(Analysis.MagnitudeAt(impulse, f, 48_000) / 0.01) > -20, $"notch at {f} Hz");
        }
    }

    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Symmetric_circuits_make_odd_harmonics_and_asymmetric_ones_even(int type) {
        var circuit = DriveCircuits.All[type - DriveBlock.CircuitTypes];
        var input = Signals.CoherentSine(32, 8_192, 0.2f); // about 190 Hz
        using var block = Create(type, 6f, 10f);

        var rendered = BlockRender.Mono(block, Signals.Concat(input, input, input), 64).AsSpan(16_384);
        var magnitudes = Fft.Magnitudes(rendered);
        var second = Analysis.ToDb(magnitudes[64] / magnitudes[32]);
        var third = Analysis.ToDb(magnitudes[96] / magnitudes[32]);

        output.WriteLine($"{Name(type)}: H2 {second:0.0} dBc, H3 {third:0.0} dBc, symmetric {circuit.IsSymmetric}");
        if (circuit.IsSymmetric) {
            Assert.True(second < -80, $"H2 at {second:0.0} dBc from a symmetric circuit");
            Assert.True(third > -60, $"H3 at {third:0.0} dBc");
        } else {
            Assert.True(second > -40, $"H2 at {second:0.0} dBc from an asymmetric circuit");
        }

        Assert.True(Math.Abs(rendered.ToArray().Average()) < 1e-3, "DC offset");
    }

    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Tone_tilts_the_spectrum_towards_the_treble(int type) {
        var noise = Signals.Noise(48_000, 3, 0.05f);

        double Tilt(float tone) {
            using var block = Create(type, 5f, tone);
            var rendered = BlockRender.Mono(block, noise, 64).AsSpan(8_192);
            var magnitudes = Fft.Magnitudes(rendered[..32_768]);
            double low = 0, high = 0;
            for (var k = 1; k < magnitudes.Length; k++) {
                var hz = k * 48_000.0 / 32_768;
                var energy = magnitudes[k] * magnitudes[k];
                if (hz is > 100 and < 500) {
                    low += energy;
                } else if (hz is > 2_000 and < 8_000) {
                    high += energy;
                }
            }

            return 10 * Math.Log10(high / low);
        }

        var dark = Tilt(0f);
        var bright = Tilt(10f);
        output.WriteLine($"{Name(type)}: treble/bass {dark:0.0} dB at tone 0, {bright:0.0} dB at tone 10");
        Assert.True(bright - dark > 3, $"tone moves the tilt by {bright - dark:0.0} dB");
    }

    [Fact]
    public void Cpu_per_type_is_reported() {
        const int Frames = 64;
        const int Blocks = 48_000 / Frames; // one second of audio
        var left = Signals.Noise(Frames, 1, 0.3f);
        var right = Signals.Noise(Frames, 2, 0.3f);
        var context = new BlockContext(48_000, 120, 0);
        var types = DriveBlock.CircuitTypes + DriveCircuits.All.Count;
        var load = new double[types];

        // Two passes: the first one lets tiered compilation settle on the shared code paths.
        for (var pass = 0; pass < 2; pass++) {
            for (var type = 0; type < types; type++) {
                using var block = Create(type, 10f);
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < Blocks; i++) {
                    block.Process(new(left, right), context);
                }

                load[type] = watch.Elapsed.TotalSeconds;
            }
        }

        for (var type = 0; type < types; type++) {
            output.WriteLine($"{type,2} {Name(type),-16} {load[type]:P2} of one core (stereo, 48 kHz, {Frames} frames)");
        }

        Assert.True(load.Max() < 0.25, $"worst type uses {load.Max():P1} of a core");
    }

    static string Name(int type) => DriveBlock.Info.Params[DriveBlock.Type].Options![type];

    static DriveBlock Create(int type, float drive, float tone = 10f, bool antialias = true) {
        var block = new DriveBlock { Antialias = antialias };
        block.Params.SetTarget(DriveBlock.Type, type);
        block.Params.SetTarget(DriveBlock.Drive, drive);
        block.Params.SetTarget(DriveBlock.Tone, tone);
        block.Prepare(new(48_000, 64));
        return block;
    }
}
