namespace Cathode.Dsp.Routing;

/// <summary>
///     The audio interface's extra channels for an external effects loop (docs/04 Phase 6 item 5): what FX Loop blocks
///     send out of the device and what comes back in. The engine copies the device's return channels in at the start of
///     each callback, sets <see cref="Offset" /> before each sub-block, and copies the send channels out at the end.
///     Several FX Loop blocks (or the old graph during a preset change) add into the same send channels.
/// </summary>
public sealed class HardwareLoop : IDisposable {
    /// <summary>Largest callback the native shim delivers (CTH_MAX_FRAMES).</summary>
    public const int MaxFrames = 4_096;

    readonly NativeBuffer sendLeft = new(MaxFrames);
    readonly NativeBuffer sendRight = new(MaxFrames);
    readonly NativeBuffer returnLeft = new(MaxFrames);
    readonly NativeBuffer returnRight = new(MaxFrames);

    /// <summary>Where the current sub-block starts within the callback.</summary>
    public int Offset { get; set; }

    /// <summary>Whether the device has return channels configured; without them blocks hear silence.</summary>
    public bool HasReturn { get; private set; }

    /// <summary>Audio thread, at the start of a callback: take the device's return channels and clear the sends.</summary>
    public void BeginCallback(ReadOnlySpan<float> returnLeft, ReadOnlySpan<float> returnRight, int frames) {
        HasReturn = !returnLeft.IsEmpty;
        if (HasReturn) {
            returnLeft.CopyTo(this.returnLeft.Span);
            (returnRight.IsEmpty ? returnLeft : returnRight).CopyTo(this.returnRight.Span);
        } else {
            this.returnLeft.Span[..frames].Clear();
            this.returnRight.Span[..frames].Clear();
        }

        sendLeft.Span[..frames].Clear();
        sendRight.Span[..frames].Clear();
        Offset = 0;
    }

    /// <summary>Audio thread, at the end of a callback: hand the summed sends to the device.</summary>
    public void EndCallback(Span<float> sendLeft, Span<float> sendRight) {
        this.sendLeft.Span[..sendLeft.Length].CopyTo(sendLeft);
        this.sendRight.Span[..sendRight.Length].CopyTo(sendRight);
    }

    /// <summary>Audio thread, from a block: add to the send channels for the current sub-block.</summary>
    public void Send(ReadOnlySpan<float> left, ReadOnlySpan<float> right, float gain) {
        var l = sendLeft.Span.Slice(Offset, left.Length);
        var r = sendRight.Span.Slice(Offset, right.Length);
        for (var i = 0; i < left.Length; i++) {
            l[i] += left[i] * gain;
            r[i] += right[i] * gain;
        }
    }

    /// <summary>Audio thread, from a block: the return channels for the current sub-block.</summary>
    public ReadOnlySpan<float> ReturnLeft(int frames) => returnLeft.Span.Slice(Offset, frames);

    public ReadOnlySpan<float> ReturnRight(int frames) => returnRight.Span.Slice(Offset, frames);

    public void Dispose() {
        sendLeft.Dispose();
        sendRight.Dispose();
        returnLeft.Dispose();
        returnRight.Dispose();
    }
}
