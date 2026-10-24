using System.Diagnostics;
using System.Numerics;
using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Wdf;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Stream T: the amps' own tremolos. The phase-shift oscillator against the textbook ladder and each model's rate
///     range against an independent solve of its drawing's ladder; the depth at full intensity and the rate it runs at;
///     intensity 0 leaving every model exactly as it is without its tremolo; a smooth onset; the panel's names; no
///     allocation; the cost.
/// </summary>
public class TremoloTests(ITestOutputHelper output) {
    const double Rate = 48_000;

    static readonly string[] Names = [
        "Deluxe 6G3", "Vibrolux 6G11", "Scout GA-17RVT", "Princeton AA964", "Princeton Reverb", "Deluxe Reverb",
        "Twin Reverb", "Super Reverb", "Vibroverb AB763", "Vibrolux Reverb AA964", "Bandmaster AB763",
        "Tremolux AA763", "Vibro-King", "18 W 1974X"
    ];

    static readonly string[] KindNames = ["Deluxe 6G3", "Vibrolux 6G11", "Deluxe Reverb", "Vibro-King", "18 W 1974X"];

    /// <summary>The models with a tremolo of their own.</summary>
    public static TheoryData<string> Models => new(Names);

    /// <summary>One model of each kind: bias (direct and through a follower), optical, the 18 W's oscillating tube.</summary>
    public static TheoryData<string> Kinds => new(KindNames);

    /// <summary>
    ///     Three equal sections (R, C) from an ideal source into an open grid oscillate at 1 / (2π RC √6): the textbook
    ///     result the solver must reproduce.
    /// </summary>
    [Fact]
    public void The_oscillator_solves_the_textbook_ladder() {
        const double r = 1e6;
        const double c = 0.01e-6;
        var design = new PhaseShiftOscillatorDesign(
            TriodeModel.Ecc83,
            300,
            1e-3, // an ideal source: no plate resistance
            1.5e3,
            c,
            c,
            c,
            r,
            r,
            r,
            new(1e-6, PotTaper.Linear)
        );
        var expected = 1 / (2 * Math.PI * r * c * Math.Sqrt(6));
        var rate = design.Rate(0.5);
        output.WriteLine($"rate {rate:0.0000} Hz, textbook {expected:0.0000} Hz");
        Assert.Equal(expected, rate, expected * 1e-6);
    }

    /// <summary>
    ///     Each model's slowest and fastest rate, from its drawing's ladder, Speed rheostat and plate: the ladder turns the
    ///     phase by exactly 180° there, checked by an independent nodal solve of the same network; every rate lies in the
    ///     range tube-amp tremolos run at.
    /// </summary>
    [Theory]
    [MemberData(nameof(Models))]
    public void The_rate_range_follows_the_drawing(string model) {
        foreach (var oscillator in Oscillators(Design(model))) {
            var (slowest, fastest) = oscillator.RateRange();
            var (from, to) = oscillator.RunningRange();
            output.WriteLine(
                $"{model}: {slowest:0.00} to {fastest:0.00} Hz (Speed {from * 10:0.0} to {to * 10:0.0}); grid swing "
                + $"{oscillator.GridSwing(from):0.0} to {oscillator.GridSwing(to):0.0} V, plate "
                + $"{oscillator.PlateSwing(from):0} V, harmonics {oscillator.Harmonic(2, 0.5):0.00} "
                + $"{oscillator.Harmonic(3, 0.5):0.00}"
            );
            Assert.InRange(slowest, 1.5, 6);
            Assert.InRange(fastest, 5, 14);
            Assert.True(fastest > 1.5 * slowest);
            foreach (var speed in new[] { from, to }) {
                var hertz = oscillator.Rate(speed);
                var phase = Loop(oscillator, hertz, speed).Phase;
                Assert.True(Math.Abs(Math.Abs(phase) - Math.PI) < 1e-6, $"phase {phase} at {hertz} Hz");
            }
        }
    }

    /// <summary>
    ///     The AB763's lamp driver idles at the cathode voltage its class-C pulses hold it at: the drawings mark 17 V there
    ///     with the vibrato running (the Deluxe Reverb's and the Twin's "+17.0V").
    /// </summary>
    [Fact]
    public void The_lamp_driver_sits_where_the_drawings_measure_it() {
        var vibrato = Optical(Design("Deluxe Reverb"));
        var bias = vibrato.DriverBias(0.5);
        output.WriteLine(
            $"driver cathode {vibrato.DriverBias(0):0.0} / {bias:0.0} / {vibrato.DriverBias(1):0.0} V at Speed 0, 5, 10; "
            + $"lamp peak {vibrato.PeakLampCurrent(0.5) * 1e3:0.00} mA, cell "
            + $"{vibrato.Cell.Resistance(vibrato.PeakLampCurrent(0.5)):0} ohm"
        );
        Assert.InRange(vibrato.DriverBias(0), 13, 21);
        Assert.InRange(vibrato.PeakLampCurrent(0.5), 0.3e-3, 3e-3);
    }

    /// <summary>
    ///     With the tremolo at full intensity a steady note swells and dips by several decibels at the rate asked for;
    ///     at intensity 0 it is steady.
    /// </summary>
    [Theory]
    [MemberData(nameof(Models))]
    public void Full_intensity_modulates_the_note_at_the_rate(string model) {
        var (slowest, fastest) = Oscillators(Design(model)).First().RateRange();
        var hertz = Math.Clamp(5, slowest, fastest);
        var (swing, measured) = Envelope(model, 100, hertz);
        var (still, _) = Envelope(model, 0, hertz);
        output.WriteLine($"{model}: {swing:0.0} dB at {measured:0.00} Hz (asked {hertz:0.00}); at 0, {still:0.000} dB");
        Assert.True(swing > 3, $"only {swing:0.0} dB");
        Assert.InRange(measured, 0.85 * hertz, 1.15 * hertz);
        Assert.True(still < 0.05, $"{still:0.000} dB at intensity 0");
    }

    /// <summary>
    ///     At intensity 0 each model renders sample for sample what the same model without its tremolo renders, whatever
    ///     the rate: the tremolo leaves no trace when it is off.
    /// </summary>
    [Theory]
    [MemberData(nameof(Models))]
    public void Intensity_zero_is_the_model_without_its_tremolo(string model) {
        var design = Design(model);
        var stripped = Strip(design);
        Assert.False(new AmpChannel(stripped, 96_000).HasTremolo);
        var input = CircuitAmpBlockTests.Chord(24_000, 0.5f);
        foreach (var switch1 in new[] { false, true }) {
            var with = new AmpChannel(design, 96_000);
            var without = new AmpChannel(stripped, 96_000);
            for (var block = 0; block * 64 < input.Length; block++) {
                var controls = new AmpControls(0.7, 0.5, 0.5, 0.5, 1, false, Switch1: switch1);
                var power = new PowerControls(0.5, 0, 0, 0.5, 0.5, 0.5);
                with.Configure(
                    controls with { TremoloRate = 3 + block % 7, TremoloDepth = 0 },
                    power with { TremoloRate = 3 + block % 5 }
                );
                without.Configure(controls, power);
                if (block == 0) {
                    with.Reset();
                    without.Reset();
                }

                for (var n = block * 64; n < Math.Min(input.Length, (block + 1) * 64); n++) {
                    var x = 2.0 * input[n];
                    Assert.Equal(without.Tick(x), with.Tick(x));
                }
            }
        }
    }

    /// <summary>
    ///     Turning the intensity from 0 to full on a steady note fades the tremolo in: no sample-to-sample step at the
    ///     onset is larger than the steady note's or the steady tremolo's own.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_onset_is_smooth(string model) {
        const int Switch = 24_000;
        using var block = Create(model, (CircuitAmpBlock.TremoloRate, 6f), (CircuitAmpBlock.Switch1, 1f));
        var input = Signals.Sine(220, Rate, 96_000, 0.05f);
        var rendered = new float[input.Length];
        for (var offset = 0; offset < input.Length; offset += 64) {
            if (offset == Switch) {
                block.Params.SetTarget(CircuitAmpBlock.TremoloDepth, 100);
            }

            var chunk = BlockRender.Mono(block, input[offset..(offset + 64)], 64);
            chunk.CopyTo(rendered, offset);
        }

        static double MaxStep(ReadOnlySpan<float> x) {
            var max = 0.0;
            for (var i = 1; i < x.Length; i++) {
                max = Math.Max(max, Math.Abs(x[i] - x[i - 1]));
            }

            return max;
        }

        var before = MaxStep(rendered.AsSpan(Switch - 9_600, 9_600));
        var onset = MaxStep(rendered.AsSpan(Switch - 1, 4_800));
        var steady = MaxStep(rendered.AsSpan(input.Length - 24_000));
        output.WriteLine($"{model}: largest step {before:0.0000} before, {onset:0.0000} at the onset, {steady:0.0000} after");
        Assert.True(Analysis.AllFinite(rendered));
        Assert.True(onset <= 1.05 * Math.Max(before, steady), $"a step of {onset:0.0000} at the onset");
    }

    /// <summary>The panel names the tremolo's knobs on the models that have one, and keeps the generic names elsewhere.</summary>
    [Fact]
    public void The_panel_names_the_tremolo() {
        Assert.Equal("Intensity", Label("tremdepth", "Deluxe Reverb"));
        Assert.Equal("Intensity", Label("tremdepth", "Deluxe 6G3"));
        Assert.Equal("Depth", Label("tremdepth", "Scout GA-17RVT"));
        Assert.Equal("Depth", Label("tremdepth", "18 W 1974X"));
        Assert.Equal("Bias Trem Rate", Label("tremrate", "Plexi 100W"));
        Assert.Equal("Bias Trem Depth", Label("tremdepth", "Plexi 100W"));
        foreach (var name in Names) {
            Assert.True(new AmpChannel(Design(name), 96_000).HasTremolo, name);
            Assert.Equal("Speed", Label("tremrate", name));
        }
    }

    /// <summary>Running and turning the tremolos allocates nothing on the audio thread.</summary>
    [Fact]
    public void The_tremolos_do_not_allocate() {
        var blocks = KindNames.Select(k => Create(k, (CircuitAmpBlock.Switch1, 1f))).ToArray();
        var input = Signals.Sine(220, Rate, 64, 0.1f);
        var buffer = new float[64];

        void Round() {
            foreach (var block in blocks) {
                for (var i = 0; i < 200; i++) {
                    block.Params.SetTarget(CircuitAmpBlock.TremoloDepth, i < 100 ? 40 + i % 60 : 0);
                    block.Params.SetTarget(CircuitAmpBlock.TremoloRate, 2 + i % 9);
                    input.CopyTo(buffer, 0);
                    block.Process(new(buffer, buffer), default);
                }
            }
        }

        Round();
        Round();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Round();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        foreach (var block in blocks) {
            block.Dispose();
        }

        output.WriteLine($"{allocated} bytes allocated");
        Assert.Equal(0, allocated);
    }

    /// <summary>
    ///     The oscillators are behavioural, solved at control rate: a model with its tremolo running costs about what it
    ///     costs without (best of several passes, against the same model with the tremolo off in the same run).
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_tremolo_costs_little(string model) {
        var input = CircuitAmpBlockTests.Chord(48_000, 0.25f);

        double Seconds(float depth) {
            using var block = Create(model, (CircuitAmpBlock.Switch1, 1f), (CircuitAmpBlock.TremoloDepth, depth));
            var buffer = new float[64];
            var best = double.MaxValue;
            for (var pass = 0; pass < 3; pass++) {
                var watch = Stopwatch.StartNew();
                for (var offset = 0; offset < input.Length; offset += 64) {
                    input.AsSpan(offset, 64).CopyTo(buffer);
                    block.Process(new(buffer, buffer), default);
                }

                best = Math.Min(best, watch.Elapsed.TotalSeconds);
            }

            return best;
        }

        // Warm both paths up, then take the best of several interleaved passes.
        var (off, on) = (double.MaxValue, double.MaxValue);
        for (var round = 0; round < 4; round++) {
            off = Math.Min(off, Seconds(0));
            on = Math.Min(on, Seconds(100));
        }

        output.WriteLine($"{model}: {100 * off:0.0} % off, {100 * on:0.0} % on (of one core, per second of audio)");
        Assert.True(on < 1.3 * off + 0.005, $"{on / off:0.00} times the cost");
    }

    internal static AmpDesign Design(string name) => AmpDesign.All.Single(a => a.Name == name);

    /// <summary>The model without its tremolo: the power amp's and every stage's taken out.</summary>
    static AmpDesign Strip(AmpDesign design) =>
        design with {
            Power = design.Power with { BiasTremolo = null }, Preamp = [..design.Preamp.Select(Strip)]
        };

    static AmpStageDesign Strip(AmpStageDesign stage) =>
        stage switch {
            ChannelSwitchStageDesign c => c with { Off = [..c.Off.Select(Strip)], On = [..c.On.Select(Strip)] },
            NetworkStageDesign n => n with { Tremolo = null },
            _ => stage
        };

    static OpticalTremoloDesign Optical(AmpDesign design) =>
        Networks(design.Preamp).Select(static n => n.Tremolo).OfType<OpticalTremoloDesign>().Single();

    /// <summary>
    ///     The ladder's response at the grid for a unit source behind the plate's resistance, at
    ///     <paramref name="hertz" />, by a nodal solve of the plate and the three ladder nodes: the loop inverts there
    ///     (phase ±180°) at the oscillation frequency.
    /// </summary>
    static Complex Loop(PhaseShiftOscillatorDesign d, double hertz, double speed) {
        var omega = 2 * Math.PI * hertz;
        var rheostat = (1 - d.Speed.Fraction(speed)) * d.Speed.Ohms;
        double[] legs = [
            d.Leg1 + (d.SpeedLeg == 1 ? rheostat : 0), d.Leg2 + (d.SpeedLeg == 2 ? rheostat : 0),
            d.Leg3 + (d.SpeedLeg == 3 ? rheostat : 0)
        ];
        Complex[] caps = [new(0, omega * d.Ladder1), new(0, omega * d.Ladder2), new(0, omega * d.Ladder3)];
        var (_, plateVolts, cathode) =
            OperatingPoint.CommonCathode(d.Tube, 0, d.Supply, d.PlateResistor, d.CathodeResistor);
        var (rp, _) = OperatingPoint.SmallSignal(d.Tube, -cathode, plateVolts - cathode);
        var source = d.PlateResistor * rp / (d.PlateResistor + rp);

        // Nodes: 0 the plate, 1 to 3 the ladder's (3 the grid).
        var y = new Complex[4, 4];
        var b = new Complex[4];
        y[0, 0] = 1 / source + new Complex(1 / d.PlateLoad, omega * d.PlateShunt);
        b[0] = 1 / source;
        for (var k = 0; k < 3; k++) {
            y[k, k] += caps[k];
            y[k, k + 1] -= caps[k];
            y[k + 1, k] -= caps[k];
            y[k + 1, k + 1] += caps[k] + 1 / legs[k];
        }

        for (var i = 0; i < 4; i++) {
            for (var j = i + 1; j < 4; j++) {
                var f = y[j, i] / y[i, i];
                for (var k = i; k < 4; k++) {
                    y[j, k] -= f * y[i, k];
                }

                b[j] -= f * b[i];
            }
        }

        var x = new Complex[4];
        for (var i = 3; i >= 0; i--) {
            var sum = b[i];
            for (var k = i + 1; k < 4; k++) {
                sum -= y[i, k] * x[k];
            }

            x[i] = sum / y[i, i];
        }

        return x[3];
    }

    /// <summary>
    ///     A steady 240 Hz note through <paramref name="model" /> (its tremolo channel, gain low) with the tremolo at
    ///     <paramref name="depth" /> % and <paramref name="hertz" />: the swing of its envelope (three periods of the note
    ///     per window) in dB, and the rate
    ///     of that envelope's swell (from its upward crossings of its mean).
    /// </summary>
    static (double Swing, double Hertz) Envelope(string model, float depth, double hertz) {
        using var block = Create(
            model,
            (CircuitAmpBlock.Gain, 3f),
            (CircuitAmpBlock.Switch1, 1f),
            (CircuitAmpBlock.TremoloDepth, depth),
            (CircuitAmpBlock.TremoloRate, (float)hertz)
        );
        const int Length = 144_000;
        const int Start = 48_000;
        const int Window = 600;
        var rendered = BlockRender.Mono(block, Signals.Sine(240, Rate, Length, 0.05f), 64);
        var count = (Length - Start) / Window;
        var levels = new double[count];
        for (var w = 0; w < count; w++) {
            levels[w] = Analysis.Rms(rendered.AsSpan(Start + w * Window, Window));
        }

        var db = levels.Select(static l => Analysis.ToDb(l)).ToArray();
        var mean = levels.Average();
        var crossings = new List<int>();
        for (var w = 1; w < count; w++) {
            if (levels[w - 1] < mean && levels[w] >= mean) {
                crossings.Add(w);
            }
        }

        var rate = crossings.Count > 1
            ? (crossings.Count - 1) / ((crossings[^1] - crossings[0]) * Window / Rate)
            : 0;
        return (db.Max() - db.Min(), rate);
    }

    static CircuitAmpBlock Create(string type, params (int Index, float Value)[] settings) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, AmpDesign.All.ToList().FindIndex(a => a.Name == type));
        foreach (var (index, value) in settings) {
            block.Params.SetTarget(index, value);
        }

        block.Prepare(new(Rate, 64));
        block.Reset();
        return block;
    }

    static string Label(string id, string type) {
        var parameter = CircuitAmpBlock.Info.Params.Single(p => p.Id == id);
        return parameter.TypeLabels is { } labels
            ? labels[AmpDesign.All.ToList().FindIndex(a => a.Name == type)]
            : parameter.Name;
    }

    static IEnumerable<NetworkStageDesign> Networks(IEnumerable<AmpStageDesign> stages) =>
        stages.SelectMany(static s => s switch {
                ChannelSwitchStageDesign c => Networks(c.Off).Concat(Networks(c.On)),
                NetworkStageDesign n => [n],
                _ => []
            }
        );

    static IEnumerable<PhaseShiftOscillatorDesign> Oscillators(AmpDesign design) {
        if (design.Power.BiasTremolo is { } bias) {
            yield return bias.Oscillator;
        }

        foreach (var tremolo in Networks(design.Preamp).Select(static n => n.Tremolo)) {
            switch (tremolo) {
                case OpticalTremoloDesign optical:
                    yield return optical.Oscillator;
                    break;
                case GridDriveTremoloDesign drive:
                    yield return drive.Oscillator;
                    break;
            }
        }
    }
}
