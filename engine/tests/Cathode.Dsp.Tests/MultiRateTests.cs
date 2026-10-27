using Cathode.Dsp.Amp;
using Cathode.Dsp.Blocks;
using Cathode.Testing;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Every block at every sample rate a device may run (docs/03-testing-strategy.md §3): prepared at 44.1, 88.2, 96
///     and 192 kHz and fed the same sound (a multitone, 50 Hz to 12 kHz at -20 dBFS RMS, sampled at that rate), it
///     renders finite and bounded, and its level and octave-band response from 63 Hz to 8 kHz match its own at 48 kHz.
///     A coefficient computed for 48 kHz only, a delay counted in samples, or a smoothing time in blocks shows up here
///     as a level or a tilt that moves with the rate. The Circuit Amp's models are held to the same at 44.1 kHz on
///     every build and at all the rates in the nightly run (<c>CATHODE_MULTIRATE_FULL=1</c>), as they take a while.
/// </summary>
/// <remarks>
///     The comparison is of sound, not of samples: the bands read the mean square of the second half of a one-second
///     render (after any delay or smoothing has settled), and only bands within <see cref="BandRangeDb" /> of the
///     loudest count, since a quiet band of a nonlinear block is mostly distortion products. Measured on x64 the
///     linear and time-based blocks agree within 0.6 dB in every band and 0.1 dB in level; the nonlinear ones and the
///     analysis-driven ones (vocoder, formant, auto-wah) within 2.2 dB and 0.7 dB; the amp models within 1.4 dB and
///     0.3 dB. The tolerances leave room above those.
/// </remarks>
public sealed class MultiRateTests {
    /// <summary>Every rate checked against 48 kHz. 192 kHz is the highest a device offers.</summary>
    static readonly double[] Rates = [44_100, 88_200, 96_000, 192_000];

    static readonly double[] Bands = [63, 125, 250, 500, 1_000, 2_000, 4_000, 8_000];

    /// <summary>Bands further than this below the loudest one are not compared.</summary>
    const double BandRangeDb = 30;

    /// <summary>A block silent at 48 kHz (a send, a synth with no notes) stays below this at every rate.</summary>
    const double SilentDb = -120;

    /// <summary>The input is -20 dBFS RMS: nothing at default settings comes near this.</summary>
    const double PeakBoundDb = 20;

    /// <summary>Linear and time-based blocks: band and level tolerances (dB).</summary>
    static readonly (double Band, double Level) Linear = (1, 0.5);

    /// <summary>
    ///     Nonlinear blocks and those driven by an analysis of the input (an envelope, a pitch, a filter bank), whose
    ///     internal oversampling or analysis windows change with the rate.
    /// </summary>
    static readonly (double Band, double Level) Nonlinear = (3, 1.5);

    static readonly HashSet<string> NonlinearTypes = [
        "drive", "dyndist", "vocoder", "formant", "wah", "vibe", "pitch", "ringmod", "synth", "circuitamp",
        "tonestack", "compressor", "mbcomp", "gate", "enhancer", "nam"
    ];

    /// <summary>The amp models: level within a fraction of a dB, bands within 2 dB.</summary>
    static readonly (double Band, double Level) AmpModel = (2, 0.5);

    static readonly BlockRegistry Registry = BlockRegistry.CreateDefault();

    public static TheoryData<string> BlockTypes => [..Registry.Descriptors.Select(static d => d.TypeId)];

    public static TheoryData<int> AmpTypes => [..Enumerable.Range(0, AmpDesign.All.Count)];

    [Theory]
    [MemberData(nameof(BlockTypes))]
    public void Every_block_sounds_the_same_at_every_rate(string typeId) =>
        AssertConsistent(
            typeId,
            () => Registry.Create(typeId),
            Rates,
            NonlinearTypes.Contains(typeId) ? Nonlinear : Linear
        );

    [Theory]
    [MemberData(nameof(AmpTypes))]
    public void Every_amp_model_sounds_the_same_at_44_1_kHz(int type) =>
        AssertConsistent(AmpDesign.All[type].Name, () => Amp(type), [44_100], AmpModel);

    [Theory]
    [MemberData(nameof(AmpTypes))]
    public void Every_amp_model_sounds_the_same_at_the_higher_rates(int type) {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("CATHODE_MULTIRATE_FULL") == "1",
            "nightly (CATHODE_MULTIRATE_FULL=1): every amp model at 88.2, 96 and 192 kHz"
        );
        AssertConsistent(AmpDesign.All[type].Name, () => Amp(type), [88_200, 96_000, 192_000], AmpModel);
    }

    static CircuitAmpBlock Amp(int type) {
        var block = new CircuitAmpBlock();
        block.Params.SetTarget(CircuitAmpBlock.Type, type);
        return block;
    }

    static void AssertConsistent(
        string name,
        Func<IDspBlock> create,
        double[] rates,
        (double Band, double Level) tolerance
    ) {
        var reference = Measure(create, 48_000);
        Assert.True(reference.Finite, $"{name}: NaN or infinity at 48 kHz");
        var failures = new List<string>();
        foreach (var rate in rates) {
            var at = $"{name} at {rate / 1_000:0.#} kHz";
            var measured = Measure(create, rate);
            if (!measured.Finite) {
                failures.Add($"{at}: NaN or infinity");
                continue;
            }

            if (measured.PeakDb > PeakBoundDb) {
                failures.Add($"{at}: peaks at {measured.PeakDb:0.0} dBFS");
            }

            if (reference.LevelDb < SilentDb) {
                if (measured.LevelDb > SilentDb) {
                    failures.Add($"{at}: {measured.LevelDb:0.0} dB where 48 kHz is silent");
                }

                continue;
            }

            if (Math.Abs(measured.LevelDb - reference.LevelDb) > tolerance.Level) {
                failures.Add($"{at}: level {measured.LevelDb - reference.LevelDb:+0.00;-0.00} dB from 48 kHz");
            }

            var loudest = reference.Bands.Max();
            for (var b = 0; b < Bands.Length; b++) {
                var difference = measured.Bands[b] - reference.Bands[b];
                if (reference.Bands[b] > loudest - BandRangeDb && Math.Abs(difference) > tolerance.Band) {
                    failures.Add($"{at}: {Bands[b]:0} Hz band {difference:+0.0;-0.0} dB from 48 kHz");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    sealed record Measurement(bool Finite, double PeakDb, double LevelDb, double[] Bands);

    /// <summary>The block at its defaults at <paramref name="rate" />, fed the multitone for a second.</summary>
    static Measurement Measure(Func<IDspBlock> create, double rate) {
        using var block = create();
        block.Prepare(new(rate, 64));
        var input = Signals.MultiTone(rate, 1.0);
        var (left, right) = BlockRender.Stereo(block, input, input, static _ => 64, sampleRate: rate);
        var settled = (int)(0.5 * rate);
        var l = Analysis.OctaveBandLevelsDb(left.AsSpan(settled), rate, Bands);
        var r = Analysis.OctaveBandLevelsDb(right.AsSpan(settled), rate, Bands);
        // Both channels' power: a stereo effect may move sound between them.
        var bands = l.Select((v, i) => 10 * Math.Log10((Math.Pow(10, v / 10) + Math.Pow(10, r[i] / 10)) / 2))
            .ToArray();
        return new(
            Analysis.AllFinite(left) && Analysis.AllFinite(right),
            Analysis.ToDb(Math.Max(Analysis.Peak(left), Analysis.Peak(right))),
            10 * Math.Log10(bands.Sum(static b => Math.Pow(10, b / 10))),
            bands
        );
    }
}
