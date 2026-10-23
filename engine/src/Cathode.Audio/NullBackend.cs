using System.Diagnostics;
using Cathode.Engine;

namespace Cathode.Audio;

/// <summary>
///     A device-less backend that calls the processor on a dedicated thread at real-time pace with silent input.
///     Used for soak tests and API tests on CI runners, which have no sound card.
/// </summary>
public sealed class NullBackend : IDisposable {
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private Thread? _thread;
    private volatile bool _running;

    public NullBackend(int sampleRate, int blockSize) {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
    }

    /// <summary>Callbacks that started later than their deadline.</summary>
    public long LateCallbacks { get; private set; }

    public void Start(IAudioProcessor processor) {
        if (_running) {
            throw new InvalidOperationException("Already running.");
        }

        _running = true;
        _thread = new Thread(() => Run(processor)) {
            IsBackground = true, Name = "cathode-null-audio", Priority = ThreadPriority.Highest
        };
        _thread.Start();
    }

    public void Stop() {
        _running = false;
        _thread?.Join();
        _thread = null;
    }

    public void Dispose() => Stop();

    private void Run(IAudioProcessor processor) {
        var input = new float[_blockSize];
        var outLeft = new float[_blockSize];
        var outRight = new float[_blockSize];
        var period = Stopwatch.Frequency * _blockSize / _sampleRate;
        var deadline = Stopwatch.GetTimestamp() + period;
        while (_running) {
            processor.Process(new AudioIO(input, input, outLeft, outRight));
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
