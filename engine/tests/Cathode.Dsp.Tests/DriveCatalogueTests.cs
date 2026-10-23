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
        Assert.Equal(["type", "drive", "tone", "level", "mix", "inputVol"], DriveBlock.Info.Params.Select(static p => p.Id));
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
                    // A second of silence: a fuzz blocked by a hard burst (its input capacitor charged through the base) takes a
                    // few hundred milliseconds to recover, as the real pedal does.
                    var rendered = BlockRender.Mono(block, Signals.Concat(stimulus, new float[48_000]), 64);
                    Assert.True(Analysis.AllFinite(rendered), $"non-finite at drive {drive}, tone {tone}");
                    Assert.True(Analysis.ToDb(Analysis.Peak(rendered)) < 30, $"peak at drive {drive}, tone {tone}");
                    // The original voicings can still emit a few subnormals after a DC step; the circuits flush them.
                    if (type >= DriveBlock.CircuitTypes) {
                        Assert.False(Analysis.AnySubnormal(rendered), "subnormal output");
                    }

                    var tail = Analysis.ToDb(Analysis.Peak(rendered.AsSpan(rendered.Length - 4_800)));
                    var which = Array.IndexOf(stimuli, stimulus);
                    Assert.True(tail < -100, $"no decay: {tail:0} dB at drive {drive}, tone {tone}, stimulus {which}");
                }
            }
        }
    }

    /// <summary>
    ///     Aliasing at full drive, for a firmly picked note (about 770 Hz at 0.1 V) and for the extreme case the original
    ///     voicings are held to (2.5 kHz at 0.5 V). Diode clipping (in a feedback loop or to ground) has a closed-form
    ///     antiderivative and keeps 60 dB in both. Clipping that only Newton can solve, or that the antiderivative cannot
    ///     smooth into a band-limited shape, relies on oversampling alone: op-amp rails and the RAT's slewing op-amp, and
    ///     above all transistors switching hard (a Fuzz Face turns a 2.5 kHz sine into a square, whose harmonics fold back
    ///     from far above any affordable rate). Those have lower floors, recorded here so that they cannot slip further.
    /// </summary>
    [Theory]
    [MemberData(nameof(CircuitTypes))]
    public void Aliasing_stays_down_at_full_drive(int type) {
        const int MusicalBin = 1_051; // prime, about 770 Hz

        double Measure(int bin, float amplitude, bool antialias) {
            var input = Signals.CoherentSine(bin * 2, Length * 2, amplitude);
            using var block = Create(type, 10f, antialias: antialias);
            return Analysis.SignalToAliasingDb(BlockRender.Mono(block, input, 64).AsSpan(Length), bin);
        }

        var circuit = DriveCircuits.All[type - DriveBlock.CircuitTypes];
        var musical = Measure(MusicalBin, 0.1f, true);
        var exact = Measure(FundamentalBin, 0.5f, false);
        var extreme = Measure(FundamentalBin, 0.5f, true);
        var (musicalFloor, extremeFloor) = AliasingFloors(circuit);
        output.WriteLine(
            $"{Name(type)}: signal-to-aliasing {musical:0.0} dB at 770 Hz / 0.1 V; at 2.5 kHz / 0.5 V {exact:0.0} dB exact " +
            $"solve, {extreme:0.0} dB antialiased (floors {musicalFloor} / {extremeFloor} dB)"
        );
        Assert.True(musical >= musicalFloor, $"signal-to-aliasing {musical:0.0} dB at 770 Hz (floor {musicalFloor} dB)");
        Assert.True(extreme >= extremeFloor, $"signal-to-aliasing {extreme:0.0} dB at 2.5 kHz (floor {extremeFloor} dB)");
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

    /// <summary>
    ///     A rough per-type report, fresh noise in every block as tools/Cathode.Bench feeds it (the bench's numbers, on a
    ///     quiet machine, are the ones that count). The bound only catches a runaway: tests run in parallel here, so each
    ///     type keeps its quickest of three timed passes and only has to run in real time.
    /// </summary>
    [Fact]
    public void Cpu_per_type_is_reported() {
        const int Frames = 64;
        const int Blocks = 48_000 / Frames; // one second of audio
        var noise = Signals.Noise(48_000, 1, 0.25f);
        var left = new float[Frames];
        var right = new float[Frames];
        var context = new BlockContext(48_000, 120, 0);
        var types = DriveBlock.CircuitTypes + DriveCircuits.All.Count;
        var load = new double[types];
        Array.Fill(load, double.MaxValue);

        // The first pass lets tiered compilation settle on the shared code paths; the other three are timed.
        for (var pass = 0; pass < 4; pass++) {
            for (var type = 0; type < types; type++) {
                using var block = Create(type, 5f, 5f);
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < Blocks; i++) {
                    noise.AsSpan(i * Frames, Frames).CopyTo(left);
                    left.CopyTo(right);
                    block.Process(new(left, right), context);
                }

                if (pass > 0) {
                    load[type] = Math.Min(load[type], watch.Elapsed.TotalSeconds);
                }
            }
        }

        for (var type = 0; type < types; type++) {
            output.WriteLine($"{type,2} {Name(type),-16} {load[type]:P2} of one core (stereo, 48 kHz, {Frames} frames)");
        }

        Assert.True(load.Max() < 1, $"worst type uses {load.Max():P1} of a core");
    }

    static string Name(int type) => DriveBlock.Info.Params[DriveBlock.Type].Options![type];

    /// <summary>Signal-to-aliasing floors (770 Hz / 0.1 V, 2.5 kHz / 0.5 V) by the hardest clipping in the circuit.</summary>
    static (double Musical, double Extreme) AliasingFloors(DriveCircuit circuit) {
        static bool Railing(DriveStage s) => s.Kind == StageKind.OpAmp && s.Diodes.ForwardCount + s.Diodes.ReverseCount == 0;

        if (circuit.Stages.Any(static s => s.Kind is StageKind.Transistor or StageKind.FuzzPair)) {
            return (25, 12); // 2x, Newton, hard switching
        }

        if (circuit.Stages.Any(static s => Railing(s) && s.SlewLimited)) {
            return (55, 30); // the RAT: 2x, Newton
        }

        if (circuit.Stages.Any(Railing)) {
            return (60, 55); // 4x, rails clamped in closed form with antialiasing
        }

        return (60, 50); // diodes in closed form with antialiasing; the brightest tone settings keep the folded tail
    }

    static DriveBlock Create(int type, float drive, float tone = 10f, bool antialias = true) {
        var block = new DriveBlock { Antialias = antialias };
        block.Params.SetTarget(DriveBlock.Type, type);
        block.Params.SetTarget(DriveBlock.Drive, drive);
        block.Params.SetTarget(DriveBlock.Tone, tone);
        block.Prepare(new(48_000, 64));
        return block;
    }
}
