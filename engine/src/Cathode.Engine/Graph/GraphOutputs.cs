using Cathode.Dsp;

namespace Cathode.Engine.Graph;

/// <summary>
///     The sub-block buffers a graph writes Outputs 2-4 into (Output 1 goes to the <see cref="AudioIO" />'s own
///     output). The engine owns one for the running graph and one for the graph spilling over under it.
/// </summary>
public sealed class GraphOutputs(int maxBlockSize) : IDisposable {
    readonly NativeBuffer[] left =
        [..Enumerable.Range(0, NodeTypes.OutputCount).Select(_ => new NativeBuffer(maxBlockSize))];

    readonly NativeBuffer[] right =
        [..Enumerable.Range(0, NodeTypes.OutputCount).Select(_ => new NativeBuffer(maxBlockSize))];

    /// <summary>Output <paramref name="output" /> (1-3), the first <paramref name="frames" /> frames.</summary>
    public Span<float> Left(int output, int frames) => left[output].Span[..frames];

    public Span<float> Right(int output, int frames) => right[output].Span[..frames];

    public void Clear(int frames) {
        for (var k = 1; k < NodeTypes.OutputCount; k++) {
            left[k].Span[..frames].Clear();
            right[k].Span[..frames].Clear();
        }
    }

    public void Dispose() {
        foreach (var buffer in left.Concat(right)) {
            buffer.Dispose();
        }
    }
}

/// <summary>One block on the graph's longest input-to-output path and the latency it adds.</summary>
public sealed record LatencyStep(string Block, string Type, int Samples);

/// <summary>
///     A sine that replaces one block's output while it is on (docs/08-blocks-parity.md §2 I/O, the cab resonance
///     helper): the host sets it for a block of the running graph, nothing is added to the preset, and it fades in and
///     out over 10 ms. A graph of another generation ignores it, so a preset change turns it off.
/// </summary>
public sealed class TestTone {
    const double FadeMs = 10;

    volatile bool enabled;
    int generation = -1;
    int slot = -1;
    double frequency = 100;
    float gain = 0.1f;
    double phase;
    float mix;

    public bool Enabled => enabled;

    /// <summary>
    ///     Control thread: play <paramref name="hertz" /> at <paramref name="level" /> in place of slot
    ///     <paramref name="slotIndex" />'s output.
    /// </summary>
    public void Start(int graphGeneration, int slotIndex, double hertz, float level) {
        Volatile.Write(ref frequency, hertz);
        Volatile.Write(ref gain, level);
        if (!enabled || graphGeneration != generation || slotIndex != slot) {
            Volatile.Write(ref generation, graphGeneration);
            Volatile.Write(ref slot, slotIndex);
        }

        enabled = true;
    }

    /// <summary>Control thread: change the pitch or level while it plays.</summary>
    public void Set(double hertz, float level) {
        Volatile.Write(ref frequency, hertz);
        Volatile.Write(ref gain, level);
    }

    public void Stop() => enabled = false;

    /// <summary>Audio thread, after a block ran: replace its output when the tone is for it.</summary>
    internal void Render(int graphGeneration, int slotIndex, Span<float> left, Span<float> right, double sampleRate) {
        if (slotIndex != Volatile.Read(ref slot) || graphGeneration != Volatile.Read(ref generation)) {
            return;
        }

        var on = enabled;
        if (!on && mix == 0f) {
            return;
        }

        var step = (float)(1.0 / (FadeMs * 0.001 * sampleRate));
        var increment = 2 * Math.PI * Volatile.Read(ref frequency) / sampleRate;
        var level = Volatile.Read(ref gain);
        for (var i = 0; i < left.Length; i++) {
            mix = on ? Math.Min(1f, mix + step) : Math.Max(0f, mix - step);
            var sine = level * (float)Math.Sin(phase);
            phase += increment;
            left[i] += (sine - left[i]) * mix;
            right[i] += (sine - right[i]) * mix;
        }

        if (phase > 2 * Math.PI) {
            phase %= 2 * Math.PI;
        }
    }
}
