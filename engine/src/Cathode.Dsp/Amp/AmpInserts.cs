using System.Runtime.CompilerServices;
using Cathode.Dsp.Blocks;
using Cathode.Dsp.Filters;

namespace Cathode.Dsp.Amp;

/// <summary>
///     The Circuit Amp block's input boost: a drive pedal from <see cref="DriveCircuits" /> in front of the amp, run at
///     the amp's oversampled rate (tone network included). Off by default.
/// </summary>
public sealed class AmpBoost {
    /// <summary>The Boost Type choices after Off: drive circuits by name.</summary>
    public static readonly string[] Circuits = [
        "TS808 OD", "Super OD", "Chiron OD", "Treble Booster", "Esoteric Boost", "FET Boost"
    ];

    static readonly DriveCircuit[] Resolved = [..Circuits.Select(static n => DriveCircuits.All.Single(c => c.Name == n))];

    readonly CircuitChannel channel = new();
    DriveCircuit? circuit;
    float gain = 1f;
    float level = 1f;
    float nextGain = 1f;
    float nextLevel = 1f;
    SvfCoefficients dcBlock;

    /// <summary>Boost Type options: Off, then <see cref="Circuits" />.</summary>
    public static IReadOnlyList<string> Types { get; } = ["Off", ..Circuits];

    public bool IsOn => circuit is not null;

    public void Reset() {
        channel.Reset();
        circuit = null;
    }

    /// <summary>Per sub-block: the pedal for <paramref name="type" /> (0 = off), its Drive and Tone (0..10) and Level (dB).</summary>
    public void Configure(int type, float drive, float tone, float levelDb, double innerRate) {
        if (type <= 0 || type > Resolved.Length) {
            if (circuit is not null) {
                circuit = null;
                channel.Release();
            }

            return;
        }

        var next = Resolved[type - 1];
        var fresh = !ReferenceEquals(next, circuit);
        circuit = next;
        channel.Configure(next, drive, tone, 10, innerRate, innerRate, true);
        var to = (float)Decibels.ToGain(next.MinGainDb + (next.MaxGainDb - next.MinGainDb) * drive / 10);
        if (fresh) {
            gain = to;
            level = Decibels.ToGain(levelDb);
            dcBlock = SvfCoefficients.Design(BiquadType.HighPass, innerRate, 10, 0.7071);
        }

        nextGain = to;
        nextLevel = Decibels.ToGain(levelDb);
    }

    /// <summary>Boosts <paramref name="samples" /> (at the inner rate) in place.</summary>
    public void Process(Span<float> samples) {
        if (circuit is null) {
            return;
        }

        channel.ProcessInner(samples, gain, nextGain, dcBlock);
        channel.ProcessOuter(samples);
        GainRamp.Apply(samples, level, nextLevel);
        gain = nextGain;
        level = nextLevel;
    }
}

/// <summary>
///     The amp's input filter (off, shelves, peak, high- or low-pass) and Definition, ahead of the first stage. Definition
///     is a tilt around <see cref="DefinitionHz" />: positive values send more treble and less bass into the preamp (a
///     tighter, more defined attack, as the preamp then clips on the treble), negative the reverse; ±100 % is ±4 dB at
///     either end. There is no matching de-emphasis: the tilt is heard, as the guide's Definition is.
/// </summary>
public struct AmpInputFilter {
    public const double DefinitionHz = 650;
    const double DefinitionDb = 4;

    SvfState filter;
    SvfState low;
    SvfState high;
    SvfCoefficients filterCoefficients;
    SvfCoefficients lowCoefficients;
    SvfCoefficients highCoefficients;
    bool useFilter;
    bool useDefinition;

    /// <summary>Input Filter options.</summary>
    public static IReadOnlyList<string> Types { get; } = ["Off", "Low Shelf", "High Shelf", "Peak", "High Pass", "Low Pass"];

    public void Reset() {
        filter.Reset();
        low.Reset();
        high.Reset();
    }

    public void Design(int type, double frequency, double q, double gainDb, double definition, double rate) {
        var wasFilter = useFilter;
        useFilter = type > 0 && !(type <= 3 && gainDb == 0);
        if (useFilter) {
            var shape = type switch {
                1 => BiquadType.LowShelf,
                2 => BiquadType.HighShelf,
                3 => BiquadType.Peak,
                4 => BiquadType.HighPass,
                _ => BiquadType.LowPass
            };
            filterCoefficients = SvfCoefficients.Design(shape, rate, frequency, q, gainDb);
        } else if (wasFilter) {
            filter.Reset();
        }

        var wasDefinition = useDefinition;
        useDefinition = definition != 0;
        if (useDefinition) {
            var db = DefinitionDb * definition;
            lowCoefficients = SvfCoefficients.Design(BiquadType.LowShelf, rate, DefinitionHz, 0.5, -db);
            highCoefficients = SvfCoefficients.Design(BiquadType.HighShelf, rate, DefinitionHz, 0.5, db);
        } else if (wasDefinition) {
            low.Reset();
            high.Reset();
        }
    }

    public void Process(Span<float> samples) {
        if (useFilter) {
            filter.Process(filterCoefficients, samples);
        }

        if (useDefinition) {
            low.Process(lowCoefficients, samples);
            high.Process(highCoefficients, samples);
        }
    }
}

/// <summary>
///     The amp's output EQ: a mono <see cref="GraphicEqualizer" /> layout (constant Q, passive layouts with their shelves)
///     ticked sample by sample, so it can sit between the preamp and the power amp. Bands at 0 dB are skipped, so a flat
///     EQ is bit-transparent.
/// </summary>
public sealed class AmpEqualizer {
    readonly SvfState[] states = new SvfState[GraphicEqualizer.MaxBands];
    readonly SvfCoefficients[] coefficients = new SvfCoefficients[GraphicEqualizer.MaxBands];
    readonly bool[] active = new bool[GraphicEqualizer.MaxBands];
    int layout = -1;
    int count;
    bool isActive;

    /// <summary>True when at least one band is cut or boosted.</summary>
    public bool IsActive => isActive;

    public void Reset() {
        Array.Clear(states);
    }

    /// <summary>Designs the bands of <paramref name="layoutIndex" /> for <paramref name="gains" /> (dB), -1 for off.</summary>
    public void Design(int layoutIndex, ReadOnlySpan<float> gains, double rate) {
        if (layoutIndex != layout) {
            layout = layoutIndex;
            Reset();
        }

        isActive = false;
        if (layoutIndex < 0) {
            count = 0;
            return;
        }

        var l = GraphicEqualizer.Layouts[layoutIndex];
        count = l.Count;
        for (var b = 0; b < count; b++) {
            var gain = gains[b];
            var on = gain != 0;
            if (!on) {
                if (active[b]) {
                    states[b].Reset();
                }

                active[b] = false;
                continue;
            }

            active[b] = true;
            isActive = true;
            var type = GraphicEqualizer.BandType(l, b, false);
            var frequency = type switch {
                BiquadType.LowShelf => l.Centers[b] * 1.4142f,
                BiquadType.HighShelf => l.Centers[b] / 1.4142f,
                _ => l.Centers[b]
            };
            var q = type == BiquadType.Peak ? l.Passive ? GraphicEqualizer.VariableQ(l.Q, gain) : l.Q : 0.7071f;
            coefficients[b] = SvfCoefficients.Design(type, rate, frequency, q, gain);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x) {
        for (var b = 0; b < count; b++) {
            if (active[b]) {
                x = states[b].Tick(coefficients[b], x);
            }
        }

        return x;
    }

    public void Process(Span<float> samples) {
        if (!IsActive) {
            return;
        }

        for (var i = 0; i < samples.Length; i++) {
            samples[i] = (float)Tick(samples[i]);
        }
    }

    public void FlushDenormals() {
        for (var b = 0; b < states.Length; b++) {
            states[b].FlushDenormals();
        }
    }
}

/// <summary>
///     The Saturation switch: a diode-style soft clipper (tanh, antiderivative antialiased) between the preamp and the
///     power amp, in units of the preamp's full scale. Its small-signal gain is unity, so Drive (0..10, 0 to 30 dB into
///     the diodes) sets how early it clips, not the level.
/// </summary>
public struct AmpSaturation {
    public const float MaxDriveDb = 30f;

    AdaaClipper clipper;

    public void Reset() => clipper.Reset();

    /// <summary>One sample of preamp volts <paramref name="x" />; <paramref name="scale" /> is full scale per volt.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Tick(double x, double scale, float drive) =>
        clipper.Process(ClipperShape.Soft, (float)(x * scale * drive)) / (drive * scale);
}

/// <summary>
///     Input Dynamics: an envelope at the amp's input, around a reference of -20 dBV (a firmly picked note), turns the
///     input up or down by <c>amount × (level − reference) / 2</c> dB, held within ±12 dB. Positive amounts expand (soft
///     playing cleaner, hard playing hotter), negative compress. 0 is off.
/// </summary>
public struct AmpInputDynamics {
    const float ReferenceDb = -20f;
    const float FloorDb = -80f;
    const float LimitDb = 12f;

    float envelope;
    float attack;
    float release;

    public void Reset() => envelope = 0;

    public void Prepare(double rate) {
        attack = (float)Math.Exp(-1 / (0.001 * rate));
        release = (float)Math.Exp(-1 / (0.08 * rate));
    }

    public void Process(Span<float> samples, float amount) {
        if (amount == 0) {
            this.envelope = 0;
            return;
        }

        var envelope = this.envelope;
        for (var i = 0; i < samples.Length; i++) {
            var level = MathF.Abs(samples[i]);
            envelope = level + (envelope - level) * (level > envelope ? attack : release);
            var db = envelope > 1e-4f ? 20f * MathF.Log10(envelope) : FloorDb;
            var gainDb = Math.Clamp(amount * 0.5f * (Math.Max(db, FloorDb) - ReferenceDb), -LimitDb, LimitDb);
            samples[i] *= MathF.Pow(10f, gainDb / 20f);
        }

        this.envelope = envelope < 1e-20f ? 0 : envelope;
    }
}

/// <summary>
///     The amp's output compressor, in dBFS at the block's output: the soft-knee curve of
///     <see cref="CompressorBlock.StaticCurve" /> (6 dB knee) on a peak detector, 5 ms attack, 150 ms release. Output type
///     detects the amp's output (feed-forward); Feedback detects the compressed output (smoother, a lower effective
///     ratio). Amount (0..10) sets the ratio from 1:1 to 10:1; Clarity (0..10) high-passes the detector from 20 to 500 Hz,
///     so the low end compresses less.
/// </summary>
public struct AmpCompressor {
    public const int Off = 0;
    public const int Output = 1;
    public const int Feedback = 2;

    SvfState sidechain;
    SvfCoefficients sidechainCoefficients;
    float reduction;
    float lastGain;
    float attack;
    float release;

    /// <summary>Output compressor types.</summary>
    public static IReadOnlyList<string> Types { get; } = ["Off", "Output", "Feedback"];

    /// <summary>Current gain reduction in dB (tests, meters).</summary>
    public readonly float ReductionDb => reduction;

    public void Prepare(double rate) {
        attack = (float)Math.Exp(-1 / (0.005 * rate));
        release = (float)Math.Exp(-1 / (0.15 * rate));
    }

    public void Reset() {
        sidechain.Reset();
        reduction = 0;
        lastGain = 1;
    }

    public void Process(Span<float> samples, int type, float amount, float threshold, float clarity, double rate) {
        if (type == Off || amount <= 0) {
            if (this.reduction != 0) {
                Reset();
            }

            return;
        }

        var ratio = 1 + 0.9f * amount;
        sidechainCoefficients = SvfCoefficients.Design(
            BiquadType.HighPass,
            rate,
            20 * Math.Pow(25, clarity / 10),
            0.7071
        );
        var feedback = type == Feedback;
        var reduction = this.reduction;
        var lastGain = this.lastGain;
        for (var i = 0; i < samples.Length; i++) {
            var x = samples[i];
            var detect = (float)sidechain.Tick(sidechainCoefficients, feedback ? x * lastGain : x);
            var peak = MathF.Abs(detect);
            var db = peak > 1e-10f ? 20f * MathF.Log10(peak) : -200f;
            var target = CompressorBlock.StaticCurve(db, threshold, ratio, 6f) - db;
            reduction = target + (reduction - target) * (target < reduction ? attack : release);
            lastGain = MathF.Pow(10f, reduction / 20f);
            samples[i] = x * lastGain;
        }

        sidechain.FlushDenormals();
        this.reduction = reduction > -1e-6f ? 0 : reduction;
        this.lastGain = lastGain;
    }
}

/// <summary>
///     What the speaker does beyond its impedance (which the power amp's load models), on the block's output in units of
///     full scale:
///     <list type="bullet">
///         <item>
///             <b>Speaker Drive</b> (0..10): cone excursion. The band below 150 Hz, where the cone moves most, runs into a
///             soft limit (tanh) whose drive rises to 24 dB; a loud low note flattens, the rest passes.
///         </item>
///         <item><b>Thump</b> (0..10, up to +12 dB): the cabinet's low resonance, a peak (Q 2) at <b>Cab Resonance</b>.</item>
///         <item>
///             <b>Speaker Compression</b> (0..10): voice-coil heating. The signal's power, averaged over
///             <b>Compression Time</b>, raises the coil's resistance and so turns the output down: a full-scale sine held
///             long enough loses <c>0.6 × amount</c> dB.
///         </item>
///     </list>
///     Each part at 0 is skipped.
/// </summary>
public struct SpeakerStage {
    public const double ExcursionHz = 150;
    public const float MaxDriveDb = 24f;

    double lowState;
    double power;
    SvfState thump;
    SvfCoefficients thumpCoefficients;

    public void Reset() {
        lowState = 0;
        power = 0;
        thump.Reset();
    }

    public void Process(
        Span<float> samples,
        float drive,
        float thumpAmount,
        float resonanceHz,
        float compression,
        float timeMs,
        double rate
    ) {
        if (drive > 0) {
            var g = Math.Tan(Math.PI * ExcursionHz / rate);
            var coefficient = g / (1 + g);
            var gain = Decibels.ToGain(drive * (MaxDriveDb / 10));
            for (var i = 0; i < samples.Length; i++) {
                double x = samples[i];
                var v = (x - lowState) * coefficient;
                var low = v + lowState;
                lowState = low + v;
                samples[i] = (float)(x - low + Math.Tanh(low * gain) / gain);
            }

            if (Math.Abs(lowState) < 1e-30) {
                lowState = 0;
            }
        } else {
            lowState = 0;
        }

        if (thumpAmount > 0) {
            thumpCoefficients = SvfCoefficients.Design(BiquadType.Peak, rate, resonanceHz, 2, 1.2 * thumpAmount);
            thump.Process(thumpCoefficients, samples);
        } else {
            thump.Reset();
        }

        if (compression > 0) {
            // A full-scale sine (mean power 0.5) settles at 1 / sqrt(1 + c / 2) = -0.6 x amount dB.
            var c = 2 * (Math.Pow(10, 0.06 * compression) - 1);
            var alpha = 1 - Math.Exp(-1 / (timeMs * 0.001 * rate));
            for (var i = 0; i < samples.Length; i++) {
                double x = samples[i];
                power += (x * x - power) * alpha;
                samples[i] = (float)(x / Math.Sqrt(1 + c * power));
            }

            if (power < 1e-30) {
                power = 0;
            }
        } else {
            power = 0;
        }
    }
}
