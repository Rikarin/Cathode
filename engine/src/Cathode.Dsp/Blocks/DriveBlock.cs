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
///         oversampled (4x, or 2x for the costliest, <see cref="DriveCircuit.Oversampling" />); Drive turns each
///         circuit's gain pot (or input gain), and Tone works the pedal's own tone network at the base rate. They read
///         their knobs on a fixed grid of <see cref="SubBlock" /> frames and recompute a network's components only when a
///         smoothed knob has moved, so their output does not depend on the host's block size. Every type has the same
///         (zero) latency.
///     </para>
///     <para>
///         Input Vol is the guitar's volume knob: the source the pedal sees is the pickup behind an audio-taper volume pot
///         (<see cref="DriveCircuits.Guitar" />). Into a component-level first stage it is a source resistance, so a Fuzz
///         Face's low input impedance cleans up as the knob rolls back, as on the real pedal; elsewhere it is the level
///         and the loading of a nominal input resistance. At 10 (the default) the original voicings are untouched.
///     </para>
/// </summary>
public sealed class DriveBlock() : DspBlock(Info) {
    public const int Type = 0;
    public const int Drive = 1;
    public const int Tone = 2;
    public const int Level = 3;
    public const int Mix = 4;

    /// <summary>The guitar's volume knob, appended after the original parameters (presets store indices).</summary>
    public const int InputVolume = 5;
    public const int Balance = 6;

    /// <summary>Index of the first circuit model: the three original voicings keep indices 0-2.</summary>
    public const int CircuitTypes = 3;

    /// <summary>Frames per knob reading of the circuit types, on a grid that ignores the host's block boundaries.</summary>
    public const int SubBlock = 32;

    const int Factor = 4;

    /// <summary>The 2x half-band of the circuits that run at 2x: the first stage of <see cref="Oversampler4x" />.</summary>
    static readonly (float[], float[]) TwiceBranches =
        HalfBandStage.Split(HalfBand.Design(Oversampler4x.AttenuationDb, 0.04));

    readonly Channel left = new();
    readonly Channel right = new();
    readonly Oversampler4x dryLeft = new(1);
    readonly Oversampler4x dryRight = new(1);
    readonly HalfBandStage dryTwiceLeft = new(TwiceBranches);
    readonly HalfBandStage dryTwiceRight = new(TwiceBranches);
    float gain = 1f;
    OutputStage output;
    float mix = 1f;
    float guitar = 1f;
    float[] dry = [];
    float[] secondDry = []; // the circuit types hold both channels' dry signal at once
    float[] dryTwice = [];
    int phase; // position on the sub-block grid
    int factor = Factor; // oversampling of the configured circuit
    DriveCircuit? configured;

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
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("inputVol", "Input Vol", 0f, 10f, 10f),
            ParamDescriptor.Float("balance", "Balance", -100f, 100f, 0f, "%")
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
    ///     interleaving two independent chains lets the core overlap them. Knobs are read once per sub-block of the grid.
    /// </summary>
    void ProcessCircuits(in StereoBuffer io, int type, DriveCircuit circuit) {
        var oversampling = Oversample ? circuit.Oversampling : 1;
        if (!ReferenceEquals(circuit, configured)) {
            configured = circuit;
            if (oversampling != factor) {
                factor = oversampling; // the other factor's filters hold stale state
                ResetOversamplers();
            }
        }

        var rate = Spec.SampleRate;
        var innerRate = rate * factor;
        var dcBlock = SvfCoefficients.Design(BiquadType.HighPass, innerRate, 10, 0.7071);
        for (var offset = 0; offset < io.Length;) {
            var length = Math.Min(SubBlock - phase, io.Length - offset);
            var chunk = io.Slice(offset, length);
            var drive = Params.Advance(Drive, length);
            var tone = Params.Advance(Tone, length);
            var volume = Params.Advance(InputVolume, length);
            var mix = Params.Advance(Mix, length) / 100f;
            var level = Params.Advance(Level, length);
            var balance = Params.Advance(Balance, length);
            var gain = InputGain(type, drive);
            var guitar = (float)DriveCircuits.Guitar(volume).Level;
            left.Circuit.Configure(circuit, drive, tone, volume, innerRate, rate, Antialias);
            right.Circuit.Configure(circuit, drive, tone, volume, innerRate, rate, Antialias);

            var leftDry = dry.AsSpan(0, length);
            var rightDry = secondDry.AsSpan(0, length);
            var blend = this.mix < 1f || mix < 1f;
            if (blend) {
                CopyDry(chunk.Left, leftDry, dryLeft, dryTwiceLeft);
                CopyDry(chunk.Right, rightDry, dryRight, dryTwiceRight);
                GainRamp.Apply(leftDry, this.guitar, guitar); // the dry path hears the guitar's volume too
                GainRamp.Apply(rightDry, this.guitar, guitar);
            }

            var innerLeft = left.Up(chunk.Left, factor);
            var innerRight = right.Up(chunk.Right, factor);
            CircuitChannel.ProcessInner(left.Circuit, right.Circuit, innerLeft, innerRight, this.gain, gain, dcBlock);
            left.Down(chunk.Left, factor);
            right.Down(chunk.Right, factor);
            left.Circuit.ProcessOuter(chunk.Left);
            right.Circuit.ProcessOuter(chunk.Right);

            if (blend) {
                GainRamp.Mix(leftDry, chunk.Left, this.mix, mix);
                GainRamp.Mix(rightDry, chunk.Right, this.mix, mix);
            }

            output.Apply(chunk, level, balance);
            this.gain = gain;
            this.mix = mix;
            this.guitar = guitar;
            offset += length;
            phase = (phase + length) % SubBlock;
        }
    }

    /// <summary>The dry signal through the same up/down filters as the processed path, so the two cannot comb.</summary>
    void CopyDry(ReadOnlySpan<float> samples, Span<float> dry, Oversampler4x four, HalfBandStage two) {
        samples.CopyTo(dry);
        switch (factor) {
            case 4:
                four.Up(dry);
                four.Down(dry);
                break;
            case 2: {
                var twice = dryTwice.AsSpan(0, 2 * dry.Length);
                two.Up(dry, twice);
                two.Down(twice, dry);
                break;
            }
        }
    }

    void ResetOversamplers() {
        left.ResetOversampler();
        right.ResetOversampler();
        dryLeft.Reset();
        dryRight.Reset();
        dryTwiceLeft.Reset();
        dryTwiceRight.Reset();
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
            dryTwice = new float[2 * spec.MaxBlockSize];
        }

        left.Allocate(spec.MaxBlockSize);
        right.Allocate(spec.MaxBlockSize);
        dryLeft.Resize(spec.MaxBlockSize);
        dryRight.Resize(spec.MaxBlockSize);
    }

    protected override void OnReset() {
        left.Reset();
        right.Reset();
        ResetOversamplers();
        gain = InputGain((int)Params.Current(Type), Params.Current(Drive));
        output.Reset(Params.Current(Level), Params.Current(Balance));
        mix = Params.Current(Mix) / 100f;
        guitar = (float)DriveCircuits.Guitar(Params.Current(InputVolume)).Level;
        phase = 0;
        factor = Factor;
        configured = null;
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var type = (int)Params.Current(Type);
        if (type >= CircuitTypes) {
            ProcessCircuits(io, type, DriveCircuits.All[type - CircuitTypes]);
            return;
        }

        if (configured is not null) {
            configured = null;
            left.Circuit.Release(); // a circuit chosen again later starts discharged
            right.Circuit.Release();
            if (factor != Factor) {
                factor = Factor;
                ResetOversamplers();
            }
        }

        var rate = Spec.SampleRate;
        var innerRate = Oversample ? rate * Factor : rate;
        var shape = type switch {
            0 => ClipperShape.Soft,
            1 => ClipperShape.Hard,
            _ => ClipperShape.Asymmetric
        };
        var drive = Params.Advance(Drive, io.Length);
        var toneValue = Params.Advance(Tone, io.Length);
        var guitar = (float)DriveCircuits.Guitar(Params.Advance(InputVolume, io.Length)).Level;
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

        GainRamp.Apply(io.Left, this.guitar, guitar); // the guitar's volume: nothing at all at 10
        GainRamp.Apply(io.Right, this.guitar, guitar);
        var dry = this.dry.AsSpan(0, io.Length);
        var blend = this.mix < 1f || mix < 1f;
        ProcessChannel(left, dryLeft, io.Left, dry, blend, type, shape, gain, pre, tone, dcBlock, mix);
        ProcessChannel(right, dryRight, io.Right, dry, blend, type, shape, gain, pre, tone, dcBlock, mix);

        this.gain = gain;
        this.mix = mix;
        this.guitar = guitar;
        phase = (phase + io.Length) % SubBlock;

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }

    sealed class Channel {
        readonly Oversampler4x oversampler = new(1);
        readonly HalfBandStage twice = new(TwiceBranches);
        float[] twiceBuffer = [];
        SvfState pre;
        SvfState tone;
        SvfState dc;
        AdaaClipper clipper;

        /// <summary>State of the circuit types, which share this channel's oversampler.</summary>
        public CircuitChannel Circuit { get; } = new();

        public void Allocate(int maxBlockSize) {
            oversampler.Resize(maxBlockSize);
            if (twiceBuffer.Length != 2 * maxBlockSize) {
                twiceBuffer = new float[2 * maxBlockSize];
            }
        }

        public void ResetOversampler() {
            oversampler.Reset();
            twice.Reset();
        }

        public void Reset() {
            oversampler.Reset();
            twice.Reset();
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

        /// <summary>The span to run the circuit on: the block oversampled by <paramref name="factor" /> (1, 2 or 4).</summary>
        public Span<float> Up(Span<float> samples, int factor) {
            switch (factor) {
                case 4:
                    return oversampler.Up(samples);
                case 2: {
                    var inner = twiceBuffer.AsSpan(0, 2 * samples.Length);
                    twice.Up(samples, inner);
                    return inner;
                }
                default:
                    return samples;
            }
        }

        public void Down(Span<float> samples, int factor) {
            switch (factor) {
                case 4:
                    oversampler.Down(samples);
                    oversampler.FlushDenormals();
                    break;
                case 2:
                    twice.Down(twiceBuffer.AsSpan(0, 2 * samples.Length), samples);
                    twice.FlushDenormals();
                    break;
            }
        }
    }
}
