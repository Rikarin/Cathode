using Cathode.Dsp.Parameters;
using Cathode.Dsp.Routing;

namespace Cathode.Dsp.Blocks;

/// <summary>
///     The real-time analyser as a grid block (docs/08-blocks-parity.md §6.24): it passes its input through untouched
///     and hands the selected channel (or the sum) to the analyser, so the RTA can look at any point of the grid
///     instead of only the output. While a preset has one, the RTA view shows it. Decay, band resolution and window are
///     the analyser's own settings (<c>PUT /api/v1/rta</c>).
/// </summary>
public sealed class RtaBlock() : DspBlock(Info) {
    public const int InputSelect = 0;

    float[] mono = [];

    public static BlockDescriptor Info { get; } = new(
        "rta",
        "RTA",
        "Utility",
        [ParamDescriptor.Choice("inputSelect", "Input Select", Stereo.InputSelectOptions)],
        maxInstances: 1,
        channels: 1
    );

    /// <summary>Set by the graph compiler; null leaves the block a plain wire.</summary>
    public AnalyzerTap? Tap { get; set; }

    protected override void OnPrepare(in ProcessSpec spec) => mono = new float[spec.MaxBlockSize];

    protected override void OnReset() { }

    protected override void OnProcess(in StereoBuffer io, in BlockContext context) {
        if (Tap is not { } tap) {
            return;
        }

        var select = (int)Params.Current(InputSelect);
        var length = Math.Min(io.Length, mono.Length);
        for (var i = 0; i < length; i++) {
            mono[i] = Stereo.Select(select, io.Left[i], io.Right[i]);
        }

        tap.Write(mono.AsSpan(0, length));
    }
}
