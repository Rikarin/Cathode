using Cathode.Dsp;
using Cathode.Dsp.IO;
using Cathode.Engine.Graph;
using Cathode.Ipc;

namespace Cathode.EngineHost;

/// <summary>
///     Runs a <see cref="RenderJob" />: the input file through the job's graph in an engine of its own, as fast as it
///     goes. The graph and the values go in as the Server's <c>loadGraph</c> and <c>setParams</c> do
///     (<see cref="EngineHostService" />), so the render is the rig a freshly connected host would play.
/// </summary>
public static class OfflineReamp {
    /// <summary>Frames a callback: the engine's default buffer.</summary>
    public const int BlockSize = 64;

    /// <summary>The longest tail a job may ask for.</summary>
    public const double MaxTailSeconds = 60;

    /// <summary>
    ///     Silence played before the input and thrown away: parameter smoothing settles from the blocks' defaults to
    ///     the preset's values, as it has long done by the time anyone plays.
    /// </summary>
    const double PrerollSeconds = 0.5;

    /// <summary>
    ///     Renders the job and writes its output file; returns the frames written and their sample rate.
    /// </summary>
    /// <exception cref="GraphValidationException">The graph does not compile, or names a parameter there is not.</exception>
    /// <exception cref="InvalidDataException">The input is not a WAV file this reads, or is empty.</exception>
    public static (int Frames, int SampleRate) Run(RenderJob job, BlockRegistry? registry = null) {
        var input = WavFile.Read(job.Input);
        if (input.Frames == 0) {
            throw new InvalidDataException("The input file is empty.");
        }

        var rate = input.SampleRate;
        using var service = new EngineHostService(new(rate, BlockSize), "offline", registry, looperSeconds: 0);
        Require(service.Handle(new() { Type = IpcTypes.LoadGraph, Generation = 1, Graph = job.Graph }));
        Require(service.Handle(new() { Type = IpcTypes.SetParams, Generation = 1, Params = job.Params }));
        var latency = service.Handle(new() { Type = IpcTypes.Latency }).Latency?.GraphLatencyFrames ?? 0;

        var tail = (int)(Math.Clamp(job.TailSeconds, 0, MaxTailSeconds) * rate);
        var frames = input.Frames + tail;
        var left = input.Channels[0];
        var right = input.Channels.Length > 1 ? input.Channels[1] : left;
        var outLeft = new float[frames];
        var outRight = new float[frames];
        // The output starts where the input's first frame comes out: after the pre-roll and the graph's latency.
        var skip = (int)(PrerollSeconds * rate) + latency;
        var blockIn = (Left: new float[BlockSize], Right: new float[BlockSize]);
        var blockOut = (Left: new float[BlockSize], Right: new float[BlockSize]);
        for (var position = -skip; position < frames; position += BlockSize) {
            // The input runs ahead of the output by the latency, so output frame p is the answer to input frame p.
            Fill(left, position + latency, blockIn.Left);
            Fill(right, position + latency, blockIn.Right);
            service.Engine.Process(new(blockIn.Left, blockIn.Right, blockOut.Left, blockOut.Right));
            Keep(blockOut.Left, outLeft, position);
            Keep(blockOut.Right, outRight, position);
        }

        WavFile.Write(job.Output, new(rate, [outLeft, outRight]));
        return (frames, rate);
    }

    /// <summary>Fills <paramref name="block" /> with the source from frame <paramref name="at" />; silence outside it.</summary>
    static void Fill(float[] source, int at, Span<float> block) {
        block.Clear();
        var from = Math.Max(at, 0);
        var to = Math.Min(at + block.Length, source.Length);
        if (to > from) {
            source.AsSpan(from, to - from).CopyTo(block[(from - at)..]);
        }
    }

    /// <summary>Copies the part of <paramref name="block" /> (at frame <paramref name="at" />) inside the output.</summary>
    static void Keep(ReadOnlySpan<float> block, float[] output, int at) {
        var from = Math.Max(at, 0);
        var to = Math.Min(at + block.Length, output.Length);
        if (to > from) {
            block[(from - at)..(to - at)].CopyTo(output.AsSpan(from));
        }
    }

    static void Require(IpcMessage reply) {
        if (reply.Type == IpcTypes.Error) {
            throw new GraphValidationException(
                reply.Errors is { Count: > 0 } errors ? errors : [reply.Error ?? "The engine refused the job."]
            );
        }
    }
}
