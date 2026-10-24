using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Pitch;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Tracking synth: a <see cref="PitchTracker" /> follows the guitar and drives two oscillators (sine, triangle,
///     band-limited saw, or pulse with variable width), each with its own octave/semitone shift, fine tune and level.
///     Their sum runs through a resonant state-variable filter whose cutoff opens with the playing envelope, then a VCA
///     that follows the input envelope.
/// </summary>
/// <remarks>
///     <para>
///         The VCA's gate opens only while the tracker has recently heard a clear pitch and the input is above the
///         threshold, so the synth is exactly silent with no input or on noise, and the attack and release times shape
///         how it follows the guitar. Pitch changes glide in log frequency; a note that starts from silence jumps
///         straight to its pitch. The tracker needs about 25 ms to lock onto a new note, so the synth speaks that much
///         after the pick attack. No latency is reported: the dry signal (Mix below 100 %) is undelayed.
///     </para>
///     <para>
///         Not modelled: the Axe-Fx's third voice, random/noise waveforms, per-voice filters and pans, and hard sync.
///         Monophonic: chords give no pitch and keep the gate closed.
///     </para>
/// </remarks>
public sealed class SynthBlock() : DspBlock(Info) {
    public const int Voice1Shape = 0;
    public const int Voice1Shift = 1;
    public const int Voice1Tune = 2;
    public const int Voice1Level = 3;
    public const int Voice2Shape = 4;
    public const int Voice2Shift = 5;
    public const int Voice2Tune = 6;
    public const int Voice2Level = 7;
    public const int PulseWidth = 8;
    public const int FilterType = 9;
    public const int Cutoff = 10;
    public const int Resonance = 11;
    public const int EnvAmount = 12;
    public const int Attack = 13;
    public const int Release = 14;
    public const int Glide = 15;
    public const int Threshold = 16;
    public const int Mix = 17;
    public const int Level = 18;
    public const int Balance = 19;

    const int FilterUpdateInterval = 16;
    const double VoicedHoldSeconds = 0.05;
    const double EnvelopeReleaseSeconds = 0.03;
    const double MaxEnvelopeOctaves = 5;
    const float SilentAmplitude = 1e-7f;

    readonly PitchTracker tracker = new();
    Oscillator one;
    Oscillator two;
    SvfCoefficients filter;
    SvfState filterState;
    double logFrequency;
    double targetLogFrequency;
    int voicedHold;
    int sinceVoiced;
    float envelope;
    float envelopeRelease;
    float amplitude;
    long sampleCount;
    float mix;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "synth",
        "Synth",
        "Synth",
        [
            ParamDescriptor.Choice("voice1Shape", "Voice 1 Shape", ["Sine", "Triangle", "Saw", "Square"], 2),
            ParamDescriptor.Float("voice1Shift", "Voice 1 Shift", -24f, 24f, 0f, "st"),
            ParamDescriptor.Float("voice1Tune", "Voice 1 Tune", -50f, 50f, 0f, "cents"),
            ParamDescriptor.Float("voice1Level", "Voice 1 Level", Decibels.SilenceDb, 6f, 0f, "dB"),
            ParamDescriptor.Choice("voice2Shape", "Voice 2 Shape", ["Sine", "Triangle", "Saw", "Square"], 3),
            ParamDescriptor.Float("voice2Shift", "Voice 2 Shift", -24f, 24f, -12f, "st"),
            ParamDescriptor.Float("voice2Tune", "Voice 2 Tune", -50f, 50f, 0f, "cents"),
            ParamDescriptor.Float("voice2Level", "Voice 2 Level", Decibels.SilenceDb, 6f, Decibels.SilenceDb, "dB"),
            ParamDescriptor.Float("pulseWidth", "Pulse Width", 5f, 95f, 50f, "%"),
            ParamDescriptor.Choice("filterType", "Filter Type", ["Low Pass", "Band Pass", "High Pass"]),
            ParamDescriptor.Float("cutoff", "Cutoff", 50f, 20_000f, 2_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("resonance", "Resonance", 0f, 100f, 20f, "%"),
            ParamDescriptor.Float("envAmount", "Env Amount", 0f, 100f, 30f, "%"),
            ParamDescriptor.Float("attack", "Attack", 0.1f, 1_000f, 5f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("release", "Release", 5f, 2_000f, 150f, "ms", ParamTaper.Log),
            ParamDescriptor.Float("glide", "Glide", 0f, 500f, 20f, "ms"),
            ParamDescriptor.Float("threshold", "Threshold", -80f, -20f, -50f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
        ],
        maxInstances: 1
    );

    /// <summary>Per-sample one-pole coefficient for a time constant of <paramref name="seconds" />.</summary>
    static float Coefficient(double seconds, double rate) =>
        seconds <= 0 ? 1f : (float)(1 - Math.Exp(-1 / (seconds * rate)));

    protected override void OnPrepare(in ProcessSpec spec) {
        tracker.Prepare(spec.SampleRate);
        voicedHold = (int)(VoicedHoldSeconds * spec.SampleRate);
        envelopeRelease = (float)Math.Exp(-1 / (EnvelopeReleaseSeconds * spec.SampleRate));
    }

    protected override void OnReset() {
        tracker.Reset();
        one.Reset();
        two.Reset();
        filterState.Reset();
        logFrequency = Math.Log2(220);
        targetLogFrequency = logFrequency;
        sinceVoiced = int.MaxValue;
        envelope = 0;
        amplitude = 0;
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
        var shapeOne = (int)Params.Current(Voice1Shape);
        var shapeTwo = (int)Params.Current(Voice2Shape);
        var offsetOne = Params.Advance(Voice1Shift, frames) / 12.0 + Params.Advance(Voice1Tune, frames) / 1_200.0;
        var offsetTwo = Params.Advance(Voice2Shift, frames) / 12.0 + Params.Advance(Voice2Tune, frames) / 1_200.0;
        var levelOne = Decibels.ToGain(Params.Advance(Voice1Level, frames));
        var levelTwo = Decibels.ToGain(Params.Advance(Voice2Level, frames));
        var width = Params.Advance(PulseWidth, frames) / 100.0;
        var filterType = (int)Params.Current(FilterType) switch {
            0 => BiquadType.LowPass,
            1 => BiquadType.BandPass,
            _ => BiquadType.HighPass
        };
        var cutoff = Params.Advance(Cutoff, frames);
        var q = 0.5 * Math.Pow(20, Params.Advance(Resonance, frames) / 100.0); // 0.5 .. 10
        var envAmount = Params.Advance(EnvAmount, frames) / 100.0 * MaxEnvelopeOctaves;
        var attack = Coefficient(Params.Advance(Attack, frames) * 0.001, rate);
        var release = Coefficient(Params.Advance(Release, frames) * 0.001, rate);
        var glide = Coefficient(Params.Advance(Glide, frames) * 0.001, rate);
        var threshold = Decibels.ToGain(Params.Advance(Threshold, frames));
        var mix = Params.Advance(Mix, frames) / 100f;
        var mixStep = (mix - this.mix) / frames;
        var maxFrequency = 0.45 * rate;

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

            // Input envelope: instant attack, 30 ms release. It drives the VCA and the filter.
            var magnitude = MathF.Abs(mono);
            envelope = magnitude > envelope ? magnitude : envelope * envelopeRelease;
            var gate = sinceVoiced < voicedHold && envelope > threshold;
            var target = gate ? envelope : 0f;
            if (gate && amplitude == 0) {
                logFrequency = targetLogFrequency; // a new note from silence starts on pitch, no glide
            }

            amplitude += (target - amplitude) * (target > amplitude ? attack : release);
            if (!gate && amplitude < SilentAmplitude) {
                amplitude = 0;
            }

            logFrequency += (targetLogFrequency - logFrequency) * glide;

            // Coefficients are refreshed on a fixed sample grid, so the result never depends on the host block size.
            if ((sampleCount++ & (FilterUpdateInterval - 1)) == 0) {
                var opened = cutoff * Math.Pow(2, envAmount * Math.Min(1, 2 * amplitude));
                filter = SvfCoefficients.Design(filterType, rate, Math.Min(opened, maxFrequency), q);
            }

            float synth = 0;
            if (amplitude > 0) {
                var incrementOne = Math.Min(Math.Pow(2, logFrequency + offsetOne), maxFrequency) / rate;
                var incrementTwo = Math.Min(Math.Pow(2, logFrequency + offsetTwo), maxFrequency) / rate;
                var oscillators = levelOne * one.Next(shapeOne, incrementOne, width)
                    + levelTwo * two.Next(shapeTwo, incrementTwo, width);
                synth = (float)filterState.Tick(filter, oscillators) * amplitude;
            } else {
                filterState.Reset(); // the VCA is closed: start the next note from a clean filter
            }

            this.mix += mixStep;
            io.Left[i] = dryLeft + (synth - dryLeft) * this.mix;
            io.Right[i] = dryRight + (synth - dryRight) * this.mix;
        }

        this.mix = mix;
        filterState.FlushDenormals();
        if (envelope < 1e-20f) {
            envelope = 0;
        }

        output.Apply(io, Params.Advance(Level, frames), Params.Advance(Balance, frames));
    }

    /// <summary>Phase-accumulator oscillator; the saw and pulse are band-limited with PolyBLEP.</summary>
    struct Oscillator {
        double phase;

        public void Reset() => phase = 0;

        public float Next(int shape, double increment, double width) {
            var p = phase;
            phase += increment;
            phase -= Math.Floor(phase);
            return shape switch {
                0 => (float)Math.Sin(2 * Math.PI * p),
                1 => (float)(1 - 4 * Math.Abs(p - 0.5)),
                2 => (float)(2 * p - 1 - Blep(p, increment)),
                _ => (float)((p < width ? 1 : -1) + Blep(p, increment) - Blep(Wrap(p - width), increment))
            };
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
