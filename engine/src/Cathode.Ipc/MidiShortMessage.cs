namespace Cathode.Ipc;

/// <summary>
///     What a <c>midiOut</c> request may carry: one complete short MIDI message. That is a channel voice message (note,
///     controller, program change, pressure, pitch bend), a system common message (time code quarter frame, song
///     position, song select, tune request) or a system real-time one (clock, start, continue, stop, active sensing,
///     reset). SysEx, running status and the undefined statuses are not sent: the native senders take three bytes at
///     most and encode by status (cth_midi_send in native/cth_audio).
/// </summary>
public static class MidiShortMessage {
    /// <summary>
    ///     How many bytes the message with this status byte has, 0 when it is not a short message that can be sent.
    /// </summary>
    public static int Length(int status) =>
        status switch {
            >= 0x80 and <= 0xBF => 3, // note off, note on, poly pressure, controller
            >= 0xC0 and <= 0xDF => 2, // program change, channel pressure
            >= 0xE0 and <= 0xEF => 3, // pitch bend
            0xF1 or 0xF3 => 2, // time code quarter frame, song select
            0xF2 => 3, // song position
            0xF6 or 0xF8 or 0xFA or 0xFB or 0xFC or 0xFE or 0xFF => 1, // tune request and real-time
            _ => 0 // data bytes, SysEx (0xF0, 0xF7) and the undefined 0xF4, 0xF5, 0xF9, 0xFD
        };

    /// <summary>What is wrong with <paramref name="bytes" /> as a message to send, or null.</summary>
    public static string? Problem(IReadOnlyList<int> bytes) {
        if (bytes.Count is < 1 or > 3) {
            return "A MIDI message is 1 to 3 bytes.";
        }

        var length = Length(bytes[0]);
        if (length == 0) {
            return $"MIDI status 0x{bytes[0]:X2} is not a message that can be sent.";
        }

        if (bytes.Count != length) {
            return $"A MIDI message with status 0x{bytes[0]:X2} is {length} bytes, not {bytes.Count}.";
        }

        for (var i = 1; i < bytes.Count; i++) {
            if (bytes[i] is < 0 or > 0x7F) {
                return "MIDI data bytes are 0 to 127.";
            }
        }

        return null;
    }
}
