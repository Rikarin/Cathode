using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Resampling;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Drive pedal: three original voicings with antiderivative anti-aliased clippers, followed by a catalogue of circuit
///     models (<see cref="DriveCircuits" />) built from wave digital filters. The original voicings:
///     <list type="bullet">
///         <item>
///             <b>Overdrive</b> (TS-style): the clipped path is high-passed around 720 Hz and summed with a clean path,
///             giving the mid hump and keeping the lows tight.
///         </item>
///         <item><b>Distortion</b> (Rat-style): high gain into a hard clipper.</item>
///         <item><b>Fuzz</b>: a biased, asymmetric soft clipper followed by a DC blocker.</item>
///     </list>
///     The whole nonlinear path (pre-filter, gain, clipper, clean-path sum, DC blocker) runs at 4x the sample rate
///     through IIR half-band oversampling, then a tone control (low-pass from 600 Hz to 8 kHz), output level and mix.
///     At partial mix the dry signal takes the same up/down filtering, so the two paths cannot comb.
///     <para>
///         The circuit types (index <see cref="CircuitTypes" /> onwards) run their stages, coupling, bias and DC blocker
///         in the same 4x domain; Drive turns each circuit's gain pot (or input gain), and Tone works the circuit's own
///         tone control at the base rate. Every type has the same (zero) latency.
///     </para>
/// </summary>
public sealed class DriveBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Drive = 1;
    public const int Tone = 2;
    public const int Level = 3;
    public const int Mix = 4;

    /// <summary>Index of the first circuit model: the three original voicings keep indices 0-2.</summary>
    public const int CircuitTypes = 3;

    const int Factor = 4;

    readonly Channel left = new();
    readonly Channel right = new();
    float gain = 1f;
    float outputGain = 1f;
    float mix = 1f;
    float[] dry = [];
    float[] secondDry = []; // the circuit types hold both channels' dry signal at once

    readonly Oversampler4x dryLeft = new(1);
    readonly Oversampler4x dryRight = new(1);

    public static BlockDescriptor Info { get; } = new(
        "drive",
        "Drive",
        "Drive",
        [
            ParamDescriptor.Choice(
                "type",
                "Type",
                ["Overdrive", "Distortion", "Fuzz", ..DriveCircuits.All.Select(static c => c.Name)]
            ),
            ParamDescriptor.Float("drive", "Drive", 0f, 10f, 5f),
            ParamDescriptor.Float("tone", "Tone", 0f, 10f, 5f),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB"),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%")
        ],
        maxInstances: 4
    );

    /// <summary>Disables antiderivative anti-aliasing, for measuring what it buys.</summary>
    public bool Antialias { get; set; } = true;

    /// <summary>Disables oversampling, for measuring what it buys. Set before <see cref="IDspBlock.Prepare" />.</summary>
    public bool Oversample { get; set; } = true;

    void ProcessChannel(
        Channel channel,
        Oversampler4x dryOversampler,
        Span<float> samples,
        Span<float> dry,
        bool blend,
        int type,
        ClipperShape shape,
        float gain,
        in SvfCoefficients pre,
        in SvfCoefficients tone,
        in SvfCoefficients dcBlock,
        float mix
    ) {
        if (blend) {
            samples.CopyTo(dry);
            if (Oversample) {
                dryOversampler.Up(dry);
                dryOversampler.Down(dry); // same phase response as the processed path
            }
        }

        channel.Process(samples, Oversample, type, shape, this.gain, gain, pre, tone, dcBlock, Antialias);
        if (blend) {
            GainRamp.Mix(dry, samples, this.mix, mix);
        }
    }

    /// <summary>
    ///     The circuit types run both channels through one loop: each channel is a long dependency chain, and
    ///     interleaving two independent chains lets the core overlap them.
    /// </summary>
    void ProcessCircuits(
        in StereoBuffer io,
        DriveCircuit circuit,
        float drive,
        float tone,
        float gain,
        in SvfCoefficients dcBlock,
        float mix
    ) {
        var rate = Spec.SampleRate;
        var innerRate = Oversample ? rate * Factor : rate;
        left.Circuit.Configure(circuit, drive, tone, innerRate, rate, Antialias);
        right.Circuit.Configure(circuit, drive, tone, innerRate, rate, Antialias);

        var leftDry = dry.AsSpan(0, io.Length);
        var rightDry = secondDry.AsSpan(0, io.Length);
        var blend = this.mix < 1f || mix < 1f;
        if (blend) {
            CopyDry(io.Left, leftDry, dryLeft);
            CopyDry(io.Right, rightDry, dryRight);
        }

        var innerLeft = left.Up(io.Left, Oversample);
        var innerRight = right.Up(io.Right, Oversample);
        CircuitChannel.ProcessInner(left.Circuit, right.Circuit, innerLeft, innerRight, this.gain, gain, dcBlock);
        left.Down(io.Left, Oversample);
        right.Down(io.Right, Oversample);
        left.Circuit.ProcessOuter(io.Left);
        right.Circuit.ProcessOuter(io.Right);

        if (blend) {
            GainRamp.Mix(leftDry, io.Left, this.mix, mix);
            GainRamp.Mix(rightDry, io.Right, this.mix, mix);
        }
    }

    void CopyDry(ReadOnlySpan<float> samples, Span<float> dry, Oversampler4x oversampler) {
        samples.CopyTo(dry);
        if (Oversample) {
            oversampler.Up(dry);
            oversampler.Down(dry); // same phase response as the processed path
        }
    }

    /// <summary>Drive 0..10 mapped to a gain range per type, in dB.</summary>
    static float InputGain(int type, float drive) {
        if (type >= CircuitTypes) {
            var circuit = DriveCircuits.All[type - CircuitTypes];
            return (float)Decibels.ToGain(circuit.MinGainDb + (circuit.MaxGainDb - circuit.MinGainDb) * drive / 10);
        }

        var (minDb, maxDb) = type switch {
            0 => (0f, 40f),
            1 => (6f, 60f),
            _ => (10f, 50f)
        };
        return Decibels.ToGain(minDb + (maxDb - minDb) * drive / 10f);
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        if (dry.Length != spec.MaxBlockSize) {
            dry = new float[spec.MaxBlockSize];
            secondDry = new float[spec.MaxBlockSize];
        }

        left.Allocate(spec.MaxBlockSize);
        right.Allocate(spec.MaxBlockSize);
        dryLeft.Resize(spec.MaxBlockSize);
        dryRight.Resize(spec.MaxBlockSize);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        dryLeft.Reset();
        dryRight.Reset();
        gain = InputGain((int)Params.Current(Type), Params.Current(Drive));
        outputGain = Decibels.ToGain(Params.Current(Level));
        mix = Params.Current(Mix) / 100f;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var innerRate = Oversample ? rate * Factor : rate;
        var type = (int)Params.Current(Type);
        var shape = type switch {
            0 => ClipperShape.Soft,
            1 => ClipperShape.Hard,
            _ => ClipperShape.Asymmetric
        };
        var drive = Params.Advance(Drive, io.Length);
        var toneValue = Params.Advance(Tone, io.Length);
        var gain = InputGain(type, drive);
        var toneHz = 600f * MathF.Pow(8_000f / 600f, toneValue / 10f);
        var tone = SvfCoefficients.Design(BiquadType.LowPass, rate, toneHz, 0.7071);
        var pre = type switch {
            0 => SvfCoefficients.Design(BiquadType.HighPass, innerRate, 720, 0.7071),
            1 => SvfCoefficients.Design(BiquadType.HighPass, innerRate, 60, 0.7071),
            _ => SvfCoefficients.Design(BiquadType.HighPass, innerRate, 30, 0.7071)
        };
        var dcBlock = SvfCoefficients.Design(BiquadType.HighPass, innerRate, 10, 0.7071);
        var mix = Params.Advance(Mix, io.Length) / 100f;

        if (type >= CircuitTypes) {
            ProcessCircuits(io, DriveCircuits.All[type - CircuitTypes], drive, toneValue, gain, dcBlock, mix);
        } else {
            left.Circuit.Release(); // a circuit chosen again later starts discharged
            right.Circuit.Release();
            var dry = this.dry.AsSpan(0, io.Length);
            var blend = this.mix < 1f || mix < 1f;
            ProcessChannel(left, dryLeft, io.Left, dry, blend, type, shape, gain, pre, tone, dcBlock, mix);
            ProcessChannel(right, dryRight, io.Right, dry, blend, type, shape, gain, pre, tone, dcBlock, mix);
        }

        this.gain = gain;
        this.mix = mix;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    sealed class Channel {
        readonly Oversampler4x oversampler = new(1);
        SvfState pre;
        SvfState tone;
        SvfState dc;
        AdaaClipper clipper;

        /// <summary>State of the circuit types, which share this channel's oversampler.</summary>
        public CircuitChannel Circuit { get; } = new();

        public void Allocate(int maxBlockSize) => oversampler.Resize(maxBlockSize);

        public void Reset() {
            oversampler.Reset();
            pre.Reset();
            tone.Reset();
            dc.Reset();
            clipper.Reset();
            Circuit.Reset();
        }

        public void Process(
            Span<float> samples,
            bool oversample,
            int type,
            ClipperShape shape,
            float fromGain,
            float toGain,
            in SvfCoefficients pre,
            in SvfCoefficients tone,
            in SvfCoefficients dcBlock,
            bool antialias
        ) {
            var inner = oversample ? oversampler.Up(samples) : samples;
            var step = (toGain - fromGain) / inner.Length;
            for (var i = 0; i < inner.Length; i++) {
                var x = inner[i];
                var gain = fromGain + step * (i + 1);
                var driven = (float)this.pre.Tick(pre, x) * gain;
                var y = clipper.Process(shape, driven, antialias);
                inner[i] = type switch {
                    0 => 0.5f * (y + x), // TS: clipped mids plus the clean path
                    2 => (float)dc.Tick(dcBlock, y), // fuzz: remove the bias offset
                    _ => y
                };
            }

            if (oversample) {
                oversampler.Down(samples);
                oversampler.FlushDenormals();
            }

            for (var i = 0; i < samples.Length; i++) {
                samples[i] = (float)this.tone.Tick(tone, samples[i]);
            }

            this.pre.FlushDenormals();
            this.tone.FlushDenormals();
            dc.FlushDenormals();
        }

        /// <summary>The span to run the circuit on: the oversampled block, or the block itself.</summary>
        public Span<float> Up(Span<float> samples, bool oversample) => oversample ? oversampler.Up(samples) : samples;

        public void Down(Span<float> samples, bool oversample) {
            if (oversample) {
                oversampler.Down(samples);
                oversampler.FlushDenormals();
            }
        }
    }
}
