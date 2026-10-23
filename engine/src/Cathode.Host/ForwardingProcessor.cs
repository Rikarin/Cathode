using Cathode.Engine;

namespace Cathode.Host;

/// <summary>
///     Lets a device session open before the processor exists: the processor needs the actual buffer size, which is
///     only known once the device is open. Outputs silence until <see cref="Target" /> is set.
/// </summary>
sealed class ForwardingProcessor : IAudioProcessor {
    IAudioProcessor? target;

    public IAudioProcessor? Target {
        get => Volatile.Read(ref this.target);
        set => Volatile.Write(ref this.target, value);
    }

    public void Process(in AudioIO io) {
        var target = Target;
        if (target is null) {
            io.OutputLeft.Clear();
            io.OutputRight.Clear();
        } else {
            target.Process(io);
        }
    }
}
