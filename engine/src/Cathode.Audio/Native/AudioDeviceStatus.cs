namespace Cathode.Audio.Native;

/// <summary>
///     Whether an open device stream is still the one the engine was prepared for (<c>cth_audio_status</c>). Once it
///     leaves <see cref="Ok" /> it never returns: the host reopens the device (it exits and is restarted).
/// </summary>
public enum AudioDeviceStatus {
    Ok = 0,

    /// <summary>
    ///     Another application changed the device's sample rate, buffer size or format, or the default device changed
    ///     under a stream opened on the default: audio runs on at settings the engine was not built for.
    /// </summary>
    Changed = 1,

    /// <summary>The device was unplugged, disabled or taken away, or stopped calling back: the output is silent.</summary>
    Lost = 2
}

public static class AudioDeviceStatusNames {
    /// <summary>The status as the API reports it: <c>ok</c>, <c>changed</c> or <c>lost</c> (constant strings).</summary>
    public static string Name(this AudioDeviceStatus status) =>
        status switch {
            AudioDeviceStatus.Changed => "changed",
            AudioDeviceStatus.Lost => "lost",
            _ => "ok"
        };
}
