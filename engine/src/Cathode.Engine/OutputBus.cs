using System.Globalization;
using Cathode.Dsp;
using Cathode.Engine.Graph;

namespace Cathode.Engine;

/// <summary>
///     Where the engine's outputs go on the device (docs/02-architecture.md §5.6): Output 1 (the main output, the
///     callback's own left and right) and Outputs 2-4, each on a pair of device channels, plus the dry DI out, the
///     unprocessed input on spare channels for re-amping. The backend owns one, hands it to the engine in
///     <see cref="AudioIO.Outputs" />, and after the callback copies Outputs 2-4 and the DI from it to their channels.
///     An output with no channels is not rendered at all.
/// </summary>
public sealed class OutputBus : IDisposable {
    /// <summary>Largest callback the native shim delivers (CTH_MAX_FRAMES).</summary>
    public const int MaxFrames = 4_096;

    /// <summary>Output 1 and Outputs 2-4.</summary>
    public const int Count = NodeTypes.OutputCount;

    readonly NativeBuffer[] left = new NativeBuffer[NodeTypes.OutputCount];
    readonly NativeBuffer[] right = new NativeBuffer[NodeTypes.OutputCount];
    readonly NativeBuffer diLeft = new(MaxFrames);
    readonly NativeBuffer diRight = new(MaxFrames);
    readonly int[] pairs;

    /// <param name="pairs">
    ///     The first device channel (0-based) of each output's pair, −1 for none; Output 1 first. Missing entries are
    ///     unmapped.
    /// </param>
    /// <param name="di">The DI out's device channels (0-based): none, one (mono) or two.</param>
    public OutputBus(IReadOnlyList<int>? pairs = null, IReadOnlyList<int>? di = null) {
        this.pairs = new int[NodeTypes.OutputCount];
        for (var k = 0; k < this.pairs.Length; k++) {
            this.pairs[k] = pairs is null ? (k == 0 ? 0 : -1) : k < pairs.Count ? pairs[k] : -1;
        }

        for (var k = 1; k < NodeTypes.OutputCount; k++) {
            left[k] = new(MaxFrames);
            right[k] = new(MaxFrames);
        }

        DiChannels = di is null ? [] : [..di.Take(2)];
    }

    /// <summary>The DI out's device channels (0-based); empty for none.</summary>
    public int[] DiChannels { get; }

    public bool HasDi => DiChannels.Length > 0;

    /// <summary>
    ///     Parses the audio settings' 1-based lists: <paramref name="outputs" /> gives the first channel of Outputs 1-4
    ///     in order (<c>1,3</c> plays Output 1 on 1-2 and Output 2 on 3-4; 0 leaves one unmapped); null or empty keeps
    ///     the default, Output 1 on channels 1-2. <paramref name="di" /> lists one or two channels for the DI out.
    /// </summary>
    public static OutputBus Parse(string? outputs, string? di) {
        var pairs = List(outputs);
        return new(pairs.Length == 0 ? null : [..pairs.Select(static c => c - 1)], [..List(di).Select(static c => c - 1)]);
    }

    static int[] List(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [
                ..text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(static c => int.Parse(c, CultureInfo.InvariantCulture))
            ];

    /// <summary>The first device channel (0-based) of output <paramref name="output" /> (0-3), or −1.</summary>
    public int Pair(int output) => pairs[output];

    public bool IsMapped(int output) => pairs[output] >= 0;

    /// <summary>Output <paramref name="output" /> (1-3) for the whole callback.</summary>
    public Span<float> Left(int output) => left[output].Span;

    public Span<float> Right(int output) => right[output].Span;

    public Span<float> DiLeft => diLeft.Span;

    public Span<float> DiRight => diRight.Span;

    public void Dispose() {
        for (var k = 1; k < NodeTypes.OutputCount; k++) {
            left[k].Dispose();
            right[k].Dispose();
        }

        diLeft.Dispose();
        diRight.Dispose();
    }
}
