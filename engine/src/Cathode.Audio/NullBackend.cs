using System.Diagnostics;
using Cathode.Engine;

namespace Cathode.Audio;

/// <summary>
///     A device-less backend that calls the processor on a dedicated thread at real-time pace with silent input.
///     Used for soak tests and API tests on CI runners, which have no sound card.
/// </summary>
/// <remarks>
///     The pace is real time on average, not per callback: the thread sleeps while it is ahead and then catches up in
///     a burst, like a driver with a larger buffer. Spinning until each deadline would burn a core per instance and
///     starve small CI runners that host several test servers at once. For the same reason a thread that cannot keep
///     up (a busy machine, a graph dearer than real time) still sleeps a millisecond each time it gives up catching
///     up, rather than running flat out: at a high priority, several of those would starve every other thread in the
///     process (a test's server stalled for tens of seconds while other test projects ran).
/// </remarks>
public sealed class NullBackend : IDisposable {
    readonly int sampleRate;
    readonly int blockSize;
    readonly OutputBus? outputs;
    readonly ThreadPriority priority;
    Thread? thread;
    volatile bool running;

    /// <summary>How far behind the thread may fall (catching up in a burst) before it gives up and resynchronises.</summary>
    static readonly long MaxLag = Stopwatch.Frequency / 10;

    /// <summary>Times the thread fell more than 100 ms behind (and skipped ahead).</summary>
    public long LateCallbacks { get; private set; }

    /// <summary>A backend at <paramref name="sampleRate" /> calling back every <paramref name="blockSize" /> frames.</summary>
    /// <param name="sampleRate">Frames per second.</param>
    /// <param name="blockSize">Frames per callback.</param>
    /// <param name="outputs">Outputs 2-4 and the DI out to render (and discard), as a device with them would.</param>
    /// <param name="priority">
    ///     The callback thread's priority: highest, as an audio thread runs, for a host process; an engine inside a
    ///     server (functional tests, development) takes normal, so it never crowds out the server's own threads.
    /// </param>
    public NullBackend(
        int sampleRate,
        int blockSize,
        OutputBus? outputs = null,
        ThreadPriority priority = ThreadPriority.Highest
    ) {
        this.sampleRate = sampleRate;
        this.blockSize = blockSize;
        this.outputs = outputs;
        this.priority = priority;
    }

    public void Start(IAudioProcessor processor) {
        if (running) {
            throw new InvalidOperationException("Already running.");
        }

        running = true;
        thread = new(() => Run(processor)) {
            IsBackground = true, Name = "cathode-null-audio", Priority = priority
        };
        thread.Start();
    }

    public void Stop() {
        running = false;
        thread?.Join();
        thread = null;
    }

    public void Dispose() => Stop();

    void Run(IAudioProcessor processor) {
        var input = new float[blockSize];
        var outLeft = new float[blockSize];
        var outRight = new float[blockSize];
        var period = Stopwatch.Frequency * blockSize / sampleRate;
        var deadline = Stopwatch.GetTimestamp() + period;
        while (running) {
            processor.Process(new(input, input, outLeft, outRight) { Outputs = outputs });
            var now = Stopwatch.GetTimestamp();
            if (now - deadline > MaxLag) {
                LateCallbacks++;
                deadline = now;
                Thread.Sleep(1); // behind for good: let the rest of the machine run
            }

            while (deadline - Stopwatch.GetTimestamp() > 0) {
                Thread.Sleep(1);
            }

            deadline += period;
        }
    }
}
