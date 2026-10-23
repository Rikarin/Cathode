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
        if (this.running) {
            throw new InvalidOperationException("Already running.");
        }

        this.running = true;
        this.thread = new(() => Run(processor)) {
            IsBackground = true, Name = "cathode-null-audio", Priority = ThreadPriority.Highest
        };
        this.thread.Start();
    }

    public void Stop() {
        this.running = false;
        this.thread?.Join();
        this.thread = null;
    }

    public void Dispose() => Stop();

    void Run(IAudioProcessor processor) {
        var input = new float[this.blockSize];
        var outLeft = new float[this.blockSize];
        var outRight = new float[this.blockSize];
        var period = Stopwatch.Frequency * this.blockSize / this.sampleRate;
        var deadline = Stopwatch.GetTimestamp() + period;
        while (this.running) {
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
