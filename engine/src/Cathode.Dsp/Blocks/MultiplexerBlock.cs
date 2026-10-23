using Cathode.Dsp.Parameters;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Picks one of the rows cabled into it (or all of them), with a 10 ms crossfade, so a scene or a footswitch can
///     switch between parallel paths: two amps, a clean and a dirty chain. The graph compiler routes each input through
///     <see cref="MixInput" /> instead of summing it; <see cref="DspBlock.Process" /> then applies the level.
/// </summary>
public sealed class MultiplexerBlock() : DspBlock(Info) {
    public const int Input = 0;
    public const int Level = 1;

    /// <summary>Rows the input choice can name; inputs from other rows are only heard with "All".</summary>
    public const int Rows = 6;

    const int All = Rows;
    const double FadeMs = 10;

    readonly float[] gains = new float[Rows + 1];
    float fadeStep = 1f;
    float outputGain = 1f;

    public static BlockDescriptor Info { get; } = new(
        "mux",
        "Multiplexer",
        "Routing",
        [
            ParamDescriptor.Choice("input", "Input", ["Row 1", "Row 2", "Row 3", "Row 4", "Row 5", "Row 6", "All"]),
            ParamDescriptor.Float("level", "Level", Decibels.SilenceDb, 20f, 0f, "dB")
        ],
        maxInstances: 4
    );

    /// <summary>
    ///     Audio thread, once per sub-block for each cabled input, before <see cref="DspBlock.Process" />: adds
    ///     <paramref name="source" /> (from grid row <paramref name="row" />) into <paramref name="destination" />, faded
    ///     in or out as the selection changes.
    /// </summary>
    public void MixInput(int row, in StereoBuffer source, in StereoBuffer destination) {
        var slot = Math.Clamp(row, 0, Rows);
        var selected = (int)Params.GetTarget(Input);
        var target = selected == All || selected == row ? 1f : 0f;
        var gain = gains[slot];
        if (gain == target) {
            if (gain == 1f) {
                Add(source.Left, destination.Left);
                Add(source.Right, destination.Right);
            }

            return;
        }

        for (var i = 0; i < source.Length; i++) {
            gain = target > gain ? Math.Min(target, gain + fadeStep) : Math.Max(target, gain - fadeStep);
            destination.Left[i] += source.Left[i] * gain;
            destination.Right[i] += source.Right[i] * gain;
        }

        gains[slot] = gain;
    }

    protected override void OnPrepare(in ProcessSpec spec) =>
        fadeStep = (float)(1.0 / Math.Max(1.0, FadeMs * 0.001 * spec.SampleRate));

    protected override void OnReset() {
        var selected = (int)Params.Current(Input);
        for (var row = 0; row < gains.Length; row++) {
            gains[row] = selected == All || selected == row ? 1f : 0f;
        }

        outputGain = Decibels.ToGain(Params.Current(Level));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var outputGain = Decibels.ToGain(Params.Advance(Level, io.Length));
        GainRamp.Apply(io.Left, this.outputGain, outputGain);
        GainRamp.Apply(io.Right, this.outputGain, outputGain);
        this.outputGain = outputGain;
    }

    static void Add(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }
}
