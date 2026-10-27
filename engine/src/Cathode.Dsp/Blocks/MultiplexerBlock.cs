using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     Picks one of the rows cabled into it (or all of them), with a 10 ms crossfade, so a scene or a footswitch can
///     switch between parallel paths: two amps, a clean and a dirty chain. The graph compiler routes each input through
///     <see cref="MixInput" /> instead of summing it; <see cref="DspBlock.Process" /> then applies the level. The choice
///     can also be one of the interface's inputs (<see cref="HardwareInputs" />: Input 1–4, USB 5/6 and 7/8), heard
///     instead of the rows, so a second instrument or a DAW return can be switched in; the block still needs a cable from
///     the grid input to be part of the graph.
/// </summary>
public sealed class MultiplexerBlock() : DspBlock(Info), ISideInputBlock {
    public const int Input = 0;
    public const int Level = 1;
    public const int Balance = 2;

    /// <summary>Rows the input choice can name; inputs from other rows are only heard with "All".</summary>
    public const int Rows = 6;

    /// <summary>The first hardware input choice (Input 1); the others follow in <see cref="HardwareInputs" /> order.</summary>
    public const int FirstInput = Rows + 1;

    const int All = Rows;
    const double FadeMs = 10;

    readonly float[] gains = new float[Rows + 1];
    readonly float[] inputGains = new float[HardwareInputs.Count];
    float fadeStep = 1f;
    NativeBuffer scratch = NativeBuffer.Empty; // a hardware input fading: left, then right
    OutputStage output;

    public static BlockDescriptor Info { get; } = new(
        "mux",
        "Multiplexer",
        "Routing",
        [
            ParamDescriptor.Choice(
                "input",
                "Input",
                ["Row 1", "Row 2", "Row 3", "Row 4", "Row 5", "Row 6", "All", ..HardwareInputs.Names]
            ),
            BlockParams.Level,
            BlockParams.Balance
        ],
        maxInstances: 4
    );

    public InputPort? Inputs { get; set; }

    /// <summary>Its cabled inputs by row (<see cref="MixInput" />), and the hardware inputs (<see cref="Inputs" />).</summary>
    public SideInputs SideInputs => SideInputs.Cables | SideInputs.Hardware;

    /// <summary>Audio thread: a cabled input, through <see cref="MixInput" />.</summary>
    public void SideInput(SideInputKind kind, int row, in StereoBuffer signal, in StereoBuffer destination) =>
        MixInput(row, signal, destination);

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

    /// <summary>
    ///     Adds hardware source <paramref name="source" /> with a gain ramp from <paramref name="from" /> to
    ///     <paramref name="to" />.
    /// </summary>
    void AddRamped(InputPort port, int source, in StereoBuffer io, float from, float to) {
        if (from == to) {
            port.Add(source, io.Left, io.Right, from);
            return;
        }

        var left = scratch.Span[..io.Length];
        var right = scratch.Span.Slice(scratch.Length / 2, io.Length);
        port.Read(source, left, right);
        var step = (to - from) / io.Length;
        for (var i = 0; i < io.Length; i++) {
            var gain = from + step * (i + 1);
            io.Left[i] += left[i] * gain;
            io.Right[i] += right[i] * gain;
        }
    }

    static void Add(ReadOnlySpan<float> source, Span<float> destination) {
        for (var i = 0; i < source.Length; i++) {
            destination[i] += source[i];
        }
    }

    protected override void OnPrepare(in ProcessSpec spec) {
        fadeStep = (float)(1.0 / Math.Max(1.0, FadeMs * 0.001 * spec.SampleRate));
        scratch.Dispose();
        scratch = new(2 * spec.MaxBlockSize);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            scratch.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnReset() {
        var selected = (int)Params.Current(Input);
        for (var row = 0; row < gains.Length; row++) {
            gains[row] = selected == All || selected == row ? 1f : 0f;
        }

        for (var source = 0; source < inputGains.Length; source++) {
            inputGains[source] = selected == FirstInput + source ? 1f : 0f;
        }

        output.Reset(Params.Current(Level), Params.Current(Balance));
    }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        var selected = (int)Params.GetTarget(Input);
        for (var source = 0; source < inputGains.Length; source++) {
            var target = selected == FirstInput + source ? 1f : 0f;
            var gain = inputGains[source];
            if (gain == 0f && target == 0f) {
                continue;
            }

            if (Inputs is { } port) {
                // A whole sub-block per step: the fade is linear across it, as the rows' is per sample.
                var end = target > gain
                    ? Math.Min(target, gain + fadeStep * io.Length)
                    : Math.Max(target, gain - fadeStep * io.Length);
                AddRamped(port, source, io, gain, end);
                gain = end;
            } else {
                gain = target;
            }

            inputGains[source] = gain;
        }

        output.Apply(io, Params.Advance(Level, io.Length), Params.Advance(Balance, io.Length));
    }
}
