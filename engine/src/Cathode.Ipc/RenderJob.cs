namespace Cathode.Ipc;

/// <summary>
///     An offline render (reamp) for <c>cathode-host --render --job job.json</c>: a WAV file played into Input 1 of a
///     graph, faster than real time and with no device, by a host process of its own so the one that plays is left
///     alone. The Server writes the job from the working copy: the graph it would load into the engine, and the rig
///     and global parameter values it would send after it.
/// </summary>
public sealed class RenderJob {
    public GraphDefinition Graph { get; set; } = new();

    /// <summary>Rig and global EQ values (<see cref="IpcBlocks" />), applied once the graph is loaded.</summary>
    public List<ParamValue> Params { get; set; } = [];

    /// <summary>The WAV file to play (a DI recording). Mono feeds both channels of Input 1.</summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    ///     Where to write the result: Output 1 as a stereo 32-bit float WAV at the input's sample rate, as long as
    ///     the input plus <see cref="TailSeconds" />, and in time with it (the graph's latency is taken off the start).
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>Silence played after the input, so delays and reverbs ring out.</summary>
    public double TailSeconds { get; set; }
}
