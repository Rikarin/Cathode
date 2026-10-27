using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;
using Cathode.Dsp.Pitch;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Ring modulator: the input multiplied by a sine carrier, which replaces every input partial at f with the
///     sidebands f + fc and |f - fc| and leaves neither the input nor the carrier in the wet signal. The carrier runs
///     from a double-precision phase accumulator, so the sidebands stay clean. With <b>Tempo</b> set, the carrier
///     follows the song tempo instead (a note value per carrier cycle), which turns the effect into a polarity-flipping
///     tremolo. A high-cut tames the upper sideband before the mix.
/// </summary>
/// <remarks>
///     <para>
///         The single-sideband types split the input into its analytic pair (<see cref="HilbertPair" />) and multiply
///         by a cosine and a sine carrier, so one sideband cancels: Upper shifts every partial up by the carrier
///         frequency, Lower shifts it down (a frequency shifter; partials pushed below 0 Hz fold back). The in-phase
///         chain is an allpass, so the wet signal is phase-shifted against the dry.
///     </para>
///     <para>
///         Pitch Tracking sets the carrier to the note being played (a <see cref="PitchTracker" />) times the
///         multiplier, gliding in 10 ms; the carrier holds its last note between notes. It overrides Frequency and
///         Tempo.
///     </para>
/// </remarks>
public sealed class RingModulatorBlock() : DspBlock(Info) {
    public const int Frequency = 0;
    public const int Tempo = 1;
    public const int HighCut = 2;
    public const int Mix = 3;
    public const int Level = 4;
    public const int Balance = 5;
    public const int Multiplier = 6;
    public const int Type = 7;
    public const int Tracking = 8;

    public const int Classic = 0;
    public const int UpperSideband = 1;
    public const int LowerSideband = 2;

    const double TrackingGlideSeconds = 0.01;

    /// <summary>Carrier cycles per beat for each <see cref="Tempo" /> option after "Off".</summary>
    static readonly double[] CyclesPerBeat = [1, 2, 4, 8, 16];

    readonly PitchTracker tracker = new();
    double phase;
    double frequency;
    double trackedFrequency;
    double glide;
    HilbertPair hilbertLeft;
    HilbertPair hilbertRight;
    SvfState highCutLeft;
    SvfState highCutRight;
    float mix = 1f;
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "ringmod",
        "Ring Modulator",
        "Modulation",
        [
            ParamDescriptor.Float("frequency", "Frequency", 10f, 5_000f, 440f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("tempo", "Tempo", ["Off", "1/4", "1/8", "1/16", "1/32", "1/64"]),
            ParamDescriptor.Float("highCut", "High Cut", 200f, 20_000f, 20_000f, "Hz", ParamTaper.Log),
            BlockParams.Mix(100f),
            BlockParams.Level,
            BlockParams.Balance,
            ParamDescriptor.Float("multiplier", "Freq Multiplier", 0.25f, 4f, 1f, "x", ParamTaper.Log),
            ParamDescriptor.Choice("type", "Type", ["Classic", "Upper Sideband", "Lower Sideband"]),
            ParamDescriptor.Switch("tracking", "Pitch Tracking")
        ],
        maxInstances: 1,
        channels: 2
    );

    double CarrierFrequency(in BlockContext context, int frames) {
        var knob = Params.Advance(Frequency, frames);
        var multiplier = Params.Advance(Multiplier, frames);
        var tempo = (int)Params.Current(Tempo);
        var carrier = tempo == 0 || !(context.Tempo > 0)
            ? knob
            : Math.Clamp(context.Tempo, 20, 400) / 60 * CyclesPerBeat[tempo - 1];
        return carrier * multiplier;
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        tracker.Prepare(spec.SampleRate);
        glide = 1 - Math.Exp(-1 / (TrackingGlideSeconds * spec.SampleRate));
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            tracker.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnReset() {
        phase = 0;
        tracker.Reset();
        trackedFrequency = 0;
        hilbertLeft.Reset();
        hilbertRight.Reset();
        frequency = Params.Current(Tempo) == 0 ? Params.Current(Frequency) * Params.Current(Multiplier) : 0;
        highCutLeft.Reset();
        highCutRight.Reset();
        mix = Params.Current(Mix) / 100f;
        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var rate = Spec.SampleRate;
        var target = CarrierFrequency(context, io.Length);
        var from = frequency == 0 ? target : frequency; // a fresh tempo-synced carrier starts on time
        var step = (target - from) / io.Length;
        var highCut = SvfCoefficients.Design(BiquadType.LowPass, rate, Params.Advance(HighCut, io.Length), 0.7071);
        var mix = Params.Advance(Mix, io.Length) / 100f;
        var mixStep = (mix - this.mix) / io.Length;
        var phase = this.phase;
        var type = (int)Params.Current(Type);
        var tracking = Params.Current(Tracking) >= 0.5f;
        double multiplier = Params.Current(Multiplier);

        for (var i = 0; i < io.Length; i++) {
            var angle = 2 * Math.PI * phase;
            var carrier = Math.Sin(angle);
            var left = io.Left[i];
            var right = io.Right[i];
            double increment;
            if (tracking) {
                // Glide toward the last tracked note; before the first one the knob's frequency stands in.
                tracker.Push(0.5f * (left + right));
                var goal = tracker.Frequency > 0 ? tracker.Frequency * multiplier : from;
                trackedFrequency = trackedFrequency == 0 ? goal : trackedFrequency + (goal - trackedFrequency) * glide;
                increment = Math.Min(trackedFrequency, 0.45 * rate) / rate;
            } else {
                trackedFrequency = 0;
                increment = (from + step * (i + 1)) / rate;
            }

            phase += increment;
            phase -= Math.Floor(phase);

            var m = this.mix + mixStep * (i + 1);
            double modulatedLeft, modulatedRight;
            if (type == Classic) {
                (modulatedLeft, modulatedRight) = (left * carrier, right * carrier);
            } else {
                // Single sideband (the quadrature output leads by 90°): I cos + Q sin shifts up, I cos - Q sin down.
                var cosine = Math.Cos(angle);
                var sign = type == UpperSideband ? 1 : -1;
                var (il, ql) = hilbertLeft.Tick(left);
                var (ir, qr) = hilbertRight.Tick(right);
                modulatedLeft = il * cosine + sign * ql * carrier;
                modulatedRight = ir * cosine + sign * qr * carrier;
            }

            var wetLeft = (float)highCutLeft.Tick(highCut, modulatedLeft);
            var wetRight = (float)highCutRight.Tick(highCut, modulatedRight);
            io.Left[i] = left + (wetLeft - left) * m;
            io.Right[i] = right + (wetRight - right) * m;
        }

        highCutLeft.FlushDenormals();
        highCutRight.FlushDenormals();
        hilbertLeft.FlushDenormals();
        hilbertRight.FlushDenormals();
        this.phase = phase;
        frequency = target;
        this.mix = mix;

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
