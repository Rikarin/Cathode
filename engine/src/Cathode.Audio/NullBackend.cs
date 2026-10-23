using System.Diagnostics;
using Cathode.Engine;

namespace Cathode.Audio;

/// <summary>
///     A device-less backend that calls the processor on a dedicated thread at real-time pace with silent input.
///     Used for soak tests and API tests on CI runners, which have no sound card.
/// </summary>
public sealed class NullBackend : IDisposable {
    readonly int sampleRate;
    readonly int blockSize;
    Thread? thread;
    volatile bool running;

    /// <summary>Callbacks that started later than their deadline.</summary>
    public long LateCallbacks { get; private set; }

    public NullBackend(int sampleRate, int blockSize) {
        this.sampleRate = sampleRate;
        this.blockSize = blockSize;
    }

    public void Start(IAudioProcessor processor) {
        if (running) {
            throw new InvalidOperationException("Already running.");
        }

        running = true;
        thread = new(() => Run(processor)) {
            IsBackground = true, Name = "cathode-null-audio", Priority = ThreadPriority.Highest
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
            processor.Process(new(input, input, outLeft, outRight));
            var now = Stopwatch.GetTimestamp();
            if (now > deadline) {
                LateCallbacks++;
                deadline = now;
            }

            while (Stopwatch.GetTimestamp() < deadline) {
                Thread.SpinWait(64);
            }

            deadline += period;
        }
    }
}
