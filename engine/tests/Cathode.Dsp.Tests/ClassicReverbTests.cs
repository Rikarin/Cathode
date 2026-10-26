using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>The classic digital reverb types: the 224-style ring (hall, plate), Schroeder–Moorer combs and Gardner's room.</summary>
public class ClassicReverbTests {
    public static TheoryData<int> ClassicTypes =>
        [ReverbBlock.ClassicHall, ReverbBlock.ClassicPlate, ReverbBlock.VintageDigital, ReverbBlock.ClassicRoom];

    [Theory]
    [InlineData(ReverbBlock.ClassicHall, 1.5f)]
    [InlineData(ReverbBlock.ClassicHall, 4f)]
    [InlineData(ReverbBlock.ClassicPlate, 1f)]
    [InlineData(ReverbBlock.ClassicPlate, 3f)]
    [InlineData(ReverbBlock.VintageDigital, 1f)]
    [InlineData(ReverbBlock.VintageDigital, 3f)]
    [InlineData(ReverbBlock.ClassicRoom, 0.6f)]
    [InlineData(ReverbBlock.ClassicRoom, 1.5f)]
    public void Measured_decay_time_matches_the_setting(int type, float decaySeconds) {
        using var reverb = Create(
            (ReverbBlock.Type, type),
            (ReverbBlock.Decay, decaySeconds),
            (ReverbBlock.Damping, 20_000f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.Mix, 100f)
        );
        var length = (int)(48_000 * decaySeconds * 1.2f);

        var response = BlockRender.Mono(reverb, Signals.Impulse(length), 64);

        var t60 = SchroederT60(response, 48_000);
        Assert.InRange(t60, decaySeconds * 0.8, decaySeconds * 1.2);
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void Size_scales_the_decay_time_only_through_the_setting(int type) {
        // A bigger space keeps the set T60: the loop gains follow the loop lengths.
        using var reverb = Create(
            (ReverbBlock.Type, type),
            (ReverbBlock.Decay, 2f),
            (ReverbBlock.Size, 100f),
            (ReverbBlock.Damping, 20_000f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.Mix, 100f)
        );

        var response = BlockRender.Mono(reverb, Signals.Impulse(48_000 * 3), 64);

        Assert.InRange(SchroederT60(response, 48_000), 2 * 0.8, 2 * 1.2);
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void The_tail_is_stereo_and_decorrelated(int type) {
        using var reverb = Create((ReverbBlock.Type, type), (ReverbBlock.Mix, 100f));

        var (left, right) = BlockRender.Stereo(
            reverb,
            Signals.Impulse(48_000),
            Signals.Impulse(48_000),
            static _ => 64
        );

        double dot = 0, ll = 0, rr = 0;
        for (var i = 2_400; i < left.Length; i++) {
            dot += left[i] * right[i];
            ll += left[i] * left[i];
            rr += right[i] * right[i];
        }

        var correlation = dot / Math.Sqrt(ll * rr);
        Assert.True(Math.Abs(correlation) < 0.3, $"correlation {correlation:0.00}");
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void The_level_is_close_to_the_halls(int type) {
        (int, float)[] settings = [(ReverbBlock.Decay, 2f), (ReverbBlock.Mix, 100f)];
        using var hall = Create([..settings, (ReverbBlock.Type, ReverbBlock.Hall)]);
        using var classic = Create([..settings, (ReverbBlock.Type, type)]);
        var noise = Signals.Noise(96_000, 5, 0.5f);

        var reference = Analysis.Rms(BlockRender.Mono(hall, noise, 64).AsSpan(48_000));
        var level = Analysis.Rms(BlockRender.Mono(classic, noise, 64).AsSpan(48_000));

        Assert.InRange(Analysis.ToDb(level / reference), -4, 4);
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void High_cut_shortens_the_high_frequency_decay(int type) {
        (int, float)[] settings = [(ReverbBlock.Type, type), (ReverbBlock.Decay, 2f), (ReverbBlock.Mix, 100f)];
        using var bright = Create([..settings, (ReverbBlock.Damping, 20_000f)]);
        using var dark = Create([..settings, (ReverbBlock.Damping, 2_000f)]);
        var impulse = Signals.Impulse(48_000);

        var brightTail = BlockRender.Mono(bright, impulse, 64)[24_000..];
        var darkTail = BlockRender.Mono(dark, impulse, 64)[24_000..];

        // The room damps only in its loop, not inside its nested allpasses, so its tail keeps a little more treble.
        var hf = Analysis.MagnitudeAt(brightTail, 6_000, 48_000);
        var hfDark = Analysis.MagnitudeAt(darkTail, 6_000, 48_000);
        Assert.True(hfDark < hf * 0.15, $"6 kHz tail: bright {hf:E2}, dark {hfDark:E2}");
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void Low_freq_time_lengthens_the_bass_decay(int type) {
        using var reverb = Create(
            (ReverbBlock.Type, type),
            (ReverbBlock.Decay, 1f),
            (ReverbBlock.Damping, 20_000f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.LowCrossover, 250f),
            (ReverbBlock.LowTime, 250f),
            (ReverbBlock.Mix, 100f)
        );

        var response = BlockRender.Mono(reverb, Signals.Impulse(48_000 * 4), 64);

        var low = SchroederT60(Band(response, 80), 48_000);
        var mid = SchroederT60(Band(response, 1_500), 48_000);
        Assert.InRange(low / mid, 1.6, 3.2);
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void An_offset_at_the_input_does_not_build_up_and_the_tail_ends_without_subnormals(int type) {
        // The tanks' loops recirculate DC with a positive gain; the DC blocker ahead of them lets only the step's
        // transient in, which then dies away with the tail instead of building up.
        using var open = Create(
            (ReverbBlock.Type, type),
            (ReverbBlock.Decay, 20f),
            (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
            (ReverbBlock.Mix, 100f)
        );
        using var cut = Create((ReverbBlock.Type, type), (ReverbBlock.Decay, 20f), (ReverbBlock.Mix, 100f));
        var offset = Enumerable.Repeat(0.5f, 48_000 * 6).ToArray();

        var output = BlockRender.Mono(open, offset, 64);
        var early = Math.Abs(output.AsSpan(0, 48_000).ToArray().Average());
        var late = Math.Abs(output.AsSpan(48_000 * 5).ToArray().Average());
        Assert.True(late < early, $"mean {early:E2} in the first second, {late:E2} in the sixth");
        var heard = BlockRender.Mono(cut, offset, 64).AsSpan(48_000 * 2).ToArray().Average();
        Assert.True(Math.Abs(heard) < 0.01, $"mean with the default low cut {heard:E2}");

        using var quick = Create((ReverbBlock.Type, type), (ReverbBlock.Decay, 0.3f), (ReverbBlock.Mix, 100f));
        var tail = BlockRender.Mono(quick, Signals.Concat(Signals.Noise(4_800, 3), new float[48_000 * 20]), 64);
        Assert.DoesNotContain(tail, static s => float.IsSubnormal(s) || !float.IsFinite(s));
        Assert.All(tail[^4_800..], static s => Assert.Equal(0f, s));
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void Every_setting_at_its_maximum_stays_bounded(int type) {
        foreach (var hold in new[] { ReverbBlock.HoldOff, ReverbBlock.Stack }) {
            using var reverb = Create(
                (ReverbBlock.Type, type),
                (ReverbBlock.Decay, 20f),
                (ReverbBlock.Size, 100f),
                (ReverbBlock.Damping, 20_000f),
                (ReverbBlock.LowCut, ReverbBlock.LowCutOff),
                (ReverbBlock.LowTime, 400f),
                (ReverbBlock.HighTime, 200f),
                (ReverbBlock.InputDiffusion, 120f),
                (ReverbBlock.InputDiffusionTime, 200f),
                (ReverbBlock.EchoDensity, 100f),
                (ReverbBlock.ModDepth, 300f),
                (ReverbBlock.ModRate, 5f),
                (ReverbBlock.Hold, hold),
                (ReverbBlock.Mix, 100f)
            );
            var noise = Signals.Concat(Signals.Noise(48_000 * 4, 7), new float[48_000 * 4]);

            var output = BlockRender.Mono(reverb, noise, 64);

            Assert.All(output, static s => Assert.True(float.IsFinite(s)));
            Assert.True(Analysis.Peak(output) < 16, $"hold {hold}: peak {Analysis.Peak(output):0.00}");
        }
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void Processing_does_not_allocate(int type) {
        using var reverb = Create((ReverbBlock.Type, type), (ReverbBlock.Mix, 100f), (ReverbBlock.EarlyLevel, 50f));
        var left = Signals.Noise(64, 1);
        var right = Signals.Noise(64, 2);

        var allocated = AllocationProbe.Measure(() => {
                for (var i = 0; i < 1_000; i++) {
                    reverb.Process(new(left, right), default);
                }
            }
        );

        Assert.Equal(0, allocated);
    }

    [Theory]
    [MemberData(nameof(ClassicTypes))]
    public void Switching_types_restarts_the_tank_empty(int type) {
        using var reverb = Create((ReverbBlock.Type, type), (ReverbBlock.Mix, 100f));
        BlockRender.Mono(reverb, Signals.Impulse(9_600), 64);

        reverb.Params.SetTarget(ReverbBlock.Type, ReverbBlock.Hall);
        BlockRender.Mono(reverb, new float[4_800], 64);
        reverb.Params.SetTarget(ReverbBlock.Type, type);
        var after = BlockRender.Mono(reverb, new float[4_800], 64);

        Assert.True(Analysis.Peak(after) < 1e-6, $"stale tail: {Analysis.Peak(after):E2}");
    }

    [Fact]
    public void Controls_a_type_does_not_use_are_hidden_on_it() {
        var parameters = ReverbBlock.Info.Params;
        string Label(string id, int type) => parameters.Single(p => p.Id == id).TypeLabels![type];

        foreach (var type in new[] {
                     ReverbBlock.ClassicHall, ReverbBlock.ClassicPlate, ReverbBlock.VintageDigital,
                     ReverbBlock.ClassicRoom
                 }) {
            Assert.Equal(string.Empty, Label("quality", type));
            Assert.Equal(string.Empty, Label("wallDiffusion", type));
            Assert.Equal(string.Empty, Label("springs", type));
            Assert.NotEqual(string.Empty, Label("lowTime", type));
            Assert.NotEqual(string.Empty, Label("earlyLevel", type));
        }

        Assert.NotEqual(string.Empty, Label("modDepth", ReverbBlock.ClassicHall));
        Assert.NotEqual(string.Empty, Label("echoDensity", ReverbBlock.ClassicPlate));
        Assert.Equal(string.Empty, Label("modDepth", ReverbBlock.VintageDigital));
        Assert.NotEqual(string.Empty, Label("inputDiffusion", ReverbBlock.VintageDigital));
        Assert.Equal(string.Empty, Label("inputDiffusion", ReverbBlock.ClassicRoom));
        Assert.Equal(string.Empty, Label("modDepth", ReverbBlock.ClassicRoom));
        Assert.All(
            parameters,
            static p => Assert.True(p.TypeLabels is null || p.TypeLabels.Count == ReverbBlock.TypeCount)
        );
    }

    static float[] Band(float[] response, double frequency) {
        var band = response.ToArray();
        var c = Filters.SvfCoefficients.Design(Filters.BiquadType.BandPass, 48_000, frequency, 2);
        var state = new Filters.SvfState();
        state.Process(c, band);
        return band;
    }

    static ReverbBlock Create(params (int Index, float Value)[] settings) {
        var reverb = new ReverbBlock();
        foreach (var (index, value) in settings) {
            reverb.Params.SetTarget(index, value);
        }

        reverb.Prepare(new(48_000, 64));
        return reverb;
    }

    /// <summary>T60 from the Schroeder backward-integrated energy decay, fitted between -5 and -25 dB (T20 x 3).</summary>
    static double SchroederT60(float[] response, double sampleRate) {
        var energy = new double[response.Length];
        double running = 0;
        for (var i = response.Length - 1; i >= 0; i--) {
            running += (double)response[i] * response[i];
            energy[i] = running;
        }

        var start = Array.FindIndex(energy, e => 10 * Math.Log10(e / energy[0]) <= -5);
        var end = Array.FindIndex(energy, e => 10 * Math.Log10(e / energy[0]) <= -25);
        return 3 * (end - start) / sampleRate;
    }
}
