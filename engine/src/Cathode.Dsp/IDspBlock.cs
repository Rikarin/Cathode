using Cathode.Dsp.Parameters;

namespace Cathode.Dsp;

/// <summary>
///     The contract every processing block implements.
/// </summary>
/// <remarks>
///     <para><see cref="Prepare" /> runs on the control thread and is the only place a block may allocate.</para>
///     <para>
///         <see cref="Process" /> and <see cref="Reset" /> run on the audio thread: no allocation, no locks, no I/O.
///     </para>
///     <para>Processing is in place: the block reads its input from <c>io</c> and overwrites it with its output.</para>
/// </remarks>
public interface IDspBlock : IDisposable {
    BlockDescriptor Descriptor { get; }

    /// <summary>Parameter targets and smoothers. Targets may be written from any thread.</summary>
    ParamBank Params { get; }

    /// <summary>
    ///     Algorithmic latency the block adds, in samples. The graph compiler compensates parallel paths.
    /// </summary>
    int LatencySamples { get; }

    void Prepare(in ProcessSpec spec);

    void Reset();

    void Process(in StereoBuffer io, in BlockContext context);
}
