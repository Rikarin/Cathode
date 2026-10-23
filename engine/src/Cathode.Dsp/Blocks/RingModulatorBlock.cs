using Cathode.Dsp.Filters;
using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Ring modulator: the input multiplied by a sine carrier, which replaces every input partial at f with the sidebands
///     f + fc and |f - fc| and leaves neither the input nor the carrier in the wet signal. The carrier runs from a
///     double-precision phase accumulator, so the sidebands stay clean. With <b>Tempo</b> set, the carrier follows the
///     song tempo instead (a note value per carrier cycle), which turns the effect into a polarity-flipping tremolo.
///     A high-cut tames the upper sideband before the mix.
/// </summary>
public sealed class RingModulatorBlock() : DspBlock(Info) {
    public const int Frequency = 0;
    public const int Tempo = 1;
    public const int HighCut = 2;
    public const int Mix = 3;
    public const int Level = 4;

    /// <summary>Carrier cycles per beat for each <see cref="Tempo" /> option after "Off".</summary>
    static readonly double[] CyclesPerBeat = [1, 2, 4, 8, 16];

    double phase;
    double frequency;
    SvfState highCutLeft;
    SvfState highCutRight;
    float mix = 1f;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "ringmod",
        "Ring Modulator",
        "Modulation",
        [
            ParamDescriptor.Float("frequency", "Frequency", 10f, 5_000f, 440f, "Hz", ParamTaper.Log),
            ParamDescriptor.Choice("tempo", "Tempo", ["Off", "1/4", "1/8", "1/16", "1/32", "1/64"]),
            ParamDescriptor.Float("highCut", "High Cut", 200f, 20_000f, 20_000f, "Hz", ParamTaper.Log),
            ParamDescriptor.Float("mix", "Mix", 0f, 100f, 100f, "%"),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 1,
        channels: 2
    );

    double CarrierFrequency(in BlockContext context, int frames) {
        var knob = Params.Advance(Frequency, frames);
        var tempo = (int)Params.Current(Tempo);
        return tempo == 0 || !(context.Tempo > 0)
            ? knob
            : Math.Clamp(context.Tempo, 20, 400) / 60 * CyclesPerBeat[tempo - 1];
    }

    protected override void OnReset() {
        phase = 0;
        frequency = Params.Current(Tempo) == 0 ? Params.Current(Frequency) : 0;
        highCutLeft.Reset();
        highCutRight.Reset();
        mix = Params.Current(Mix) / 100f;
        outputGain = Decibels.ToGain(Params.Current(Level));
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

        for (var i = 0; i < io.Length; i++) {
            var carrier = Math.Sin(2 * Math.PI * phase);
            phase += (from + step * (i + 1)) / rate;
            phase -= Math.Floor(phase);

            var m = this.mix + mixStep * (i + 1);
            var left = io.Left[i];
            var right = io.Right[i];
            var wetLeft = (float)highCutLeft.Tick(highCut, left * carrier);
            var wetRight = (float)highCutRight.Tick(highCut, right * carrier);
            io.Left[i] = left + (wetLeft - left) * m;
            io.Right[i] = right + (wetRight - right) * m;
        }

        highCutLeft.FlushDenormals();
        highCutRight.FlushDenormals();
        this.phase = phase;
        frequency = target;
        this.mix = mix;

        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }
}
