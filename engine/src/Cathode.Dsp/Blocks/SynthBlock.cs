using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Pitch;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Tracking synth: a <see cref="PitchTracker" /> follows the guitar and drives three voices. Each voice has its own
///     waveform (sine, triangle, band-limited saw, pulse with its own duty cycle, a random level held for each cycle,
///     white or pink noise, or off), octave/semitone shift, fine tune, level and pan, and its own resonant
///     state-variable filter whose cutoff opens with the playing envelope, then a VCA that follows the input envelope.
/// </summary>
/// <remarks>
///     <para>
///         Each voice's Track mode sets what drives it. Pitch + Envelope: the tracked note (plus the shift) and the
///         gated input envelope. Envelope: the voice's own Frequency (plus the shift), still played by the envelope.
///         Off: a drone at its Frequency at a fixed −6 dB, independent of the input.
///     </para>
///     <para>
///         The tracked VCA's gate opens only while the tracker has recently heard a clear pitch and the input is above
///         the threshold, so a tracking voice is exactly silent with no input or on noise; the envelope-only VCA needs
///         only the threshold. Attack and release shape how both follow the guitar. Pitch changes glide in log
///         frequency; a note that starts from silence jumps straight to its pitch. The tracker needs about 25 ms to
///         lock onto a new note, so the synth speaks that much after the pick attack. No latency is reported: the dry
///         signal (Mix below 100 %) is undelayed.
///     </para>
///     <para>Not modelled: hard sync. Monophonic: chords give no pitch and keep the tracked gate closed.</para>
/// </remarks>
public sealed class SynthBlock() : DspBlock(Info) {
    // Offsets of each voice's parameters within its group.
    public const int Shape = 0;
    public const int Track = 1;
    public const int Frequency = 2;
    public const int Shift = 3;
    public const int Tune = 4;
    public const int VoiceLevel = 5;
    public const int Duty = 6;
    public const int Pan = 7;
    public const int FilterType = 8;
    public const int Cutoff = 9;
    public const int Resonance = 10;
    public const int VoiceParams = 11;

    public const int Voices = 3;

    public const int EnvAmount = Voices * VoiceParams;
    public const int Attack = EnvAmount + 1;
    public const int Release = Attack + 1;
    public const int Glide = Release + 1;
    public const int Threshold = Glide + 1;
    public const int Mix = Threshold + 1;
    public const int Level = Mix + 1;
    public const int Balance = Level + 1;
    public const int GlobalMix = Balance + 1;

    public const int Sine = 0;
    public const int Triangle = 1;
    public const int Saw = 2;
    public const int Square = 3;
    public const int Random = 4;
    public const int WhiteNoise = 5;
    public const int PinkNoise = 6;
    public const int Off = 7;

    public const int TrackPitch = 0;
    public const int TrackEnvelope = 1;
    public const int TrackOff = 2;

    const int FilterUpdateInterval = 16;
    const double VoicedHoldSeconds = 0.05;
    const double EnvelopeReleaseSeconds = 0.03;
    const double MaxEnvelopeOctaves = 5;
    const float SilentAmplitude = 1e-7f;
    const float DroneAmplitude = 0.5f;

    static readonly string[] ShapeNames =
        ["Sine", "Triangle", "Saw", "Square", "Random", "White Noise", "Pink Noise", "Off"];

    /// <summary>Default shape, shift and level of each voice: a saw, a square an octave down and a sine a fifth up.</summary>
    static readonly (int Shape, float Shift, float Level)[] Defaults =
        [(Saw, 0f, 0f), (Square, -12f, Decibels.SilenceDb), (Sine, 7f, Decibels.SilenceDb)];

    readonly PitchTracker tracker = new();
    readonly Oscillator[] oscillators = [new(1), new(2), new(3)];
    readonly SvfCoefficients[] filters = new SvfCoefficients[Voices];
    readonly SvfState[] filterStates = new SvfState[Voices];
    readonly float[] gainsLeft = new float[Voices];
    readonly float[] gainsRight = new float[Voices];
    double logFrequency;
    double targetLogFrequency;
    int voicedHold;
    int sinceVoiced;
    float envelope;
    float envelopeRelease;
    float amplitude;
    float envelopeAmplitude;
    long sampleCount;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "synth",
        "Synth",
        "Synth",
        [
            ..Enumerable.Range(0, Voices).SelectMany(VoiceDescriptors),
            ParamDescriptor.Float("envAmount", "Env Amount", 0f, 100f, 30f, "%"),
            ParamDescriptor.Float("attack", "Attack", 0.1f, 1_000f, 5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 5f, 2_000f, 150f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("glide", "Glide", 0f, 500f, 20f, "ms"),
            ParamDescriptor.Float("threshold", "Threshold", -80f, -20f, -50f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%"),
            EffectsMix.Param
        ],
        maxInstances: 1
    );

    /// <summary>
    ///     The index of parameter <paramref name="offset" /> (<see cref="Shape" /> ...) of voice
    ///     <paramref name="voice" /> (0-2).
    /// </summary>
    public static int VoiceParam(int voice, int offset) => voice * VoiceParams + offset;

    static IEnumerable<ParamDescriptor> VoiceDescriptors(int voice) {
        var (shape, shift, level) = Defaults[voice];
        var name = $"Voice {voice + 1}";
        var id = $"voice{voice + 1}";
        return [
            ParamDescriptor.Choice($"{id}Shape", $"{name} Shape", ShapeNames, shape),
            ParamDescriptor.Choice($"{id}Track", $"{name} Track", ["Pitch + Env", "Envelope", "Off"]),
            ParamDescriptor.Float($"{id}Freq", $"{name} Frequency", 20f, 10_000f, 220f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float($"{id}Shift", $"{name} Shift", -24f, 24f, shift, "st"),
            ParamDescriptor.Float($"{id}Tune", $"{name} Tune", -50f, 50f, 0f, "cents"),
            ParamDescriptor.Float($"{id}Level", $"{name} Level", Decibels.SilenceDb, 6f, level, "dB"),
            ParamDescriptor.Float($"{id}Duty", $"{name} Duty", 5f, 95f, 50f, "%"),
            ParamDescriptor.Float($"{id}Pan", $"{name} Pan", -100f, 100f, 0f, "%"),
            ParamDescriptor.Choice($"{id}FilterType", $"{name} Filter", ["Low Pass", "Band Pass", "High Pass"]),
            ParamDescriptor.Float($"{id}Cutoff", $"{name} Cutoff", 50f, 20_000f, 2_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float($"{id}Resonance", $"{name} Resonance", 0f, 100f, 20f, "%")
        ];
    }

    /// <summary>Per-sample one-pole coefficient for a time constant of <paramref name="seconds" />.</summary>
    static float Coefficient(double seconds, double rate) =>
        seconds <= 0 ? 1f : (float)(1 - Math.Exp(-1 / (seconds * rate)));

    /// <summary>A VCA following its target with the attack and release; closed and quiet, it snaps to zero.</summary>
    static float Follow(float amplitude, float target, float attack, float release, bool gate) {
        amplitude += (target - amplitude) * (target > amplitude ? attack : release);
        return !gate && amplitude < SilentAmplitude ? 0f : amplitude;
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        tracker.Prepare(spec.SampleRate);
        voicedHold = (int)(VoicedHoldSeconds * spec.SampleRate);
        envelopeRelease = (float)Math.Exp(-1 / (EnvelopeReleaseSeconds * spec.SampleRate));
    }

    protected override void OnReset() {
        tracker.Reset();
        foreach (var oscillator in oscillators) {
            oscillator.Reset();
        }

        Array.Clear(filterStates);
        logFrequency = Math.Log2(220);
        targetLogFrequency = logFrequency;
        sinceVoiced = int.MaxValue;
        envelope = 0;
        amplitude = 0;
        envelopeAmplitude = 0;
        sampleCount = 0;
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            tracker.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var frames = io.Length;
        var rate = Spec.SampleRate;
        var maxFrequency = 0.45 * rate;
        Span<int> shapes = stackalloc int[Voices];
        Span<int> tracks = stackalloc int[Voices];
        Span<double> offsets = stackalloc double[Voices];
        Span<double> manual = stackalloc double[Voices];
        Span<float> levels = stackalloc float[Voices];
        Span<double> widths = stackalloc double[Voices];
        Span<double> cutoffs = stackalloc double[Voices];
        Span<double> qs = stackalloc double[Voices];
        Span<int> filterTypes = stackalloc int[Voices];
        for (var v = 0; v < Voices; v++) {
            shapes[v] = (int)Params.Current(VoiceParam(v, Shape));
            tracks[v] = (int)Params.Current(VoiceParam(v, Track));
            offsets[v] = Params.Advance(VoiceParam(v, Shift), frames) / 12.0
                + Params.Advance(VoiceParam(v, Tune), frames) / 1_200.0;
            manual[v] = Math.Log2(Params.Advance(VoiceParam(v, Frequency), frames));
            levels[v] = Decibels.ToGain(Params.Advance(VoiceParam(v, VoiceLevel), frames));
            widths[v] = Params.Advance(VoiceParam(v, Duty), frames) / 100.0;
            var pan = Params.Advance(VoiceParam(v, Pan), frames);
            var angle = (pan / 100f + 1) * MathF.PI / 4; // constant power, unity per side at the centre
            (gainsLeft[v], gainsRight[v]) = (MathF.Sqrt(2) * MathF.Cos(angle), MathF.Sqrt(2) * MathF.Sin(angle));
            filterTypes[v] = (int)Params.Current(VoiceParam(v, FilterType));
            cutoffs[v] = Params.Advance(VoiceParam(v, Cutoff), frames);
            qs[v] = 0.5 * Math.Pow(20, Params.Advance(VoiceParam(v, Resonance), frames) / 100.0); // 0.5 .. 10
        }

        var envAmount = Params.Advance(EnvAmount, frames) / 100.0 * MaxEnvelopeOctaves;
        var attack = Coefficient(Params.Advance(Attack, frames) * 0.001, rate);
        var release = Coefficient(Params.Advance(Release, frames) * 0.001, rate);
        var glide = Coefficient(Params.Advance(Glide, frames) * 0.001, rate);
        var threshold = Decibels.ToGain(Params.Advance(Threshold, frames));
        var mix = EffectsMix.Apply(Params.Advance(Mix, frames), Params.Current(GlobalMix), context) / 100f;
        var mixStep = (mix - this.mix) / frames;

        for (var i = 0; i < frames; i++) {
            var dryLeft = io.Left[i];
            var dryRight = io.Right[i];
            var mono = 0.5f * (dryLeft + dryRight);

            if (sinceVoiced < int.MaxValue) {
                sinceVoiced++;
            }

            if (tracker.Push(mono) && tracker.Voiced) {
                sinceVoiced = 0;
                targetLogFrequency = Math.Log2(tracker.Frequency);
            }

            // Input envelope: instant attack, 30 ms release. It drives the VCAs and the filters.
            var magnitude = MathF.Abs(mono);
            envelope = magnitude > envelope ? magnitude : envelope * envelopeRelease;
            var open = envelope > threshold;
            var gate = sinceVoiced < voicedHold && open;
            var target = gate ? envelope : 0f;
            if (gate && amplitude == 0) {
                logFrequency = targetLogFrequency; // a new note from silence starts on pitch, no glide
            }

            amplitude = Follow(amplitude, target, attack, release, gate);
            envelopeAmplitude = Follow(envelopeAmplitude, open ? envelope : 0f, attack, release, open);
            logFrequency += (targetLogFrequency - logFrequency) * glide;

            // Coefficients are refreshed on a fixed sample grid, so the result never depends on the host block size.
            var refresh = (sampleCount++ & (FilterUpdateInterval - 1)) == 0;
            float synthLeft = 0, synthRight = 0;
            for (var v = 0; v < Voices; v++) {
                if (shapes[v] == Off || levels[v] == 0) {
                    continue;
                }

                var vca = tracks[v] switch {
                    TrackPitch => amplitude,
                    TrackEnvelope => envelopeAmplitude,
                    _ => DroneAmplitude
                };
                if (refresh) {
                    var opened = cutoffs[v] * Math.Pow(2, envAmount * Math.Min(1, 2 * vca));
                    var type = filterTypes[v] switch {
                        0 => BiquadType.LowPass,
                        1 => BiquadType.BandPass,
                        _ => BiquadType.HighPass
                    };
                    filters[v] = SvfCoefficients.Design(type, rate, Math.Min(opened, maxFrequency), qs[v]);
                }

                if (vca > 0) {
                    var pitch = tracks[v] == TrackPitch ? logFrequency : manual[v];
                    var increment = Math.Min(Math.Pow(2, pitch + offsets[v]), maxFrequency) / rate;
                    var raw = levels[v] * oscillators[v].Next(shapes[v], increment, widths[v]);
                    var voice = (float)filterStates[v].Tick(filters[v], raw) * vca;
                    synthLeft += voice * gainsLeft[v];
                    synthRight += voice * gainsRight[v];
                } else {
                    filterStates[v].Reset(); // the VCA is closed: start the next note from a clean filter
                }
            }

            this.mix += mixStep;
            io.Left[i] = dryLeft + (synthLeft - dryLeft) * this.mix;
            io.Right[i] = dryRight + (synthRight - dryRight) * this.mix;
        }

        this.mix = mix;
        for (var v = 0; v < Voices; v++) {
            filterStates[v].FlushDenormals();
        }

        if (envelope < 1e-20f) {
            envelope = 0;
        }

        output.Apply(io, Params.Advance(Level, frames), Params.Advance(Balance, frames));
    }

    /// <summary>
    ///     Phase-accumulator oscillator; the saw and pulse are band-limited with PolyBLEP. The random shape holds a new
    ///     level each cycle; the noises come from a xorshift generator, the pink one through Paul Kellet's economy
    ///     filter (−3 dB per octave within about 1 dB above 10 Hz). Each voice has its own seed, so resets replay.
    /// </summary>
    sealed class Oscillator(uint seed) {
        double phase;
        uint noise;
        float held;
        float pink0, pink1, pink2;

        public void Reset() {
            phase = 0;
            noise = (seed * 0x9E3779B9u) | 1;
            held = 0;
            (pink0, pink1, pink2) = (0, 0, 0);
        }

        public float Next(int shape, double increment, double width) {
            var p = phase;
            phase += increment;
            var wrapped = phase >= 1;
            phase -= Math.Floor(phase);
            switch (shape) {
                case Sine:
                    return (float)Math.Sin(2 * Math.PI * p);
                case Triangle:
                    return (float)(1 - 4 * Math.Abs(p - 0.5));
                case Saw:
                    return (float)(2 * p - 1 - Blep(p, increment));
                case Square:
                    return (float)((p < width ? 1 : -1) + Blep(p, increment) - Blep(Wrap(p - width), increment));
                case Random:
                    if (wrapped || held == 0) {
                        held = White();
                    }

                    return held;
                case WhiteNoise:
                    return White();
                default: {
                    var white = White();
                    pink0 = 0.99765f * pink0 + white * 0.0990460f;
                    pink1 = 0.96300f * pink1 + white * 0.2965164f;
                    pink2 = 0.57000f * pink2 + white * 1.0526913f;
                    return 0.25f * (pink0 + pink1 + pink2 + white * 0.1848f);
                }
            }
        }

        float White() {
            noise ^= noise << 13;
            noise ^= noise >> 17;
            noise ^= noise << 5;
            return noise * (2f / uint.MaxValue) - 1;
        }

        static double Wrap(double p) => p < 0 ? p + 1 : p;

        /// <summary>Polynomial band-limited step residual (Välimäki and Huovilainen, 2007).</summary>
        static double Blep(double t, double dt) {
            if (t < dt) {
                t /= dt;
                return t + t - t * t - 1;
            }

            if (t > 1 - dt) {
                t = (t - 1) / dt;
                return t * t + t + t + 1;
            }

            return 0;
        }
    }
}
