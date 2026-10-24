using System.Runtime.InteropServices;

namespace Cathode.Audio.Native;

/// <summary>A MIDI endpoint as the OS reports it. <see cref="Uid" /> is stable across runs.</summary>
public sealed record MidiPort(string Uid, string Name, bool IsInput);

/// <summary>A short MIDI message with its OS timestamp (nanoseconds on <see cref="NativeMidi.NowNanoseconds" />).</summary>
public readonly record struct MidiMessage(ulong TimeNs, byte Status, byte Data1, byte Data2, byte Length) {
    public int Channel => Status < 0xF0 ? (Status & 0x0F) + 1 : 0;

    public int Kind => Status < 0xF0 ? Status & 0xF0 : Status;
}

/// <summary>
///     MIDI through the <c>cth_audio</c> shim (CoreMIDI on macOS, WinMM on Windows, the ALSA sequencer on Linux). Input
///     from every source is queued natively; <see cref="Read" /> drains it from one thread.
/// </summary>
public static unsafe partial class NativeMidi {
    const string Library = "cth_audio";
    const int ReadBatch = 256;

    public static bool IsAvailable => NativeAudio.IsLibraryPresent && NativeAudio.Backend != "none";

    public static IReadOnlyList<MidiPort> Ports() {
        var ports = new List<MidiPort>();
        var count = PortCount();
        for (var i = 0; i < count; i++) {
            PortNative port;
            if (PortInfo(i, &port) == 0) {
                ports.Add(new(Text(port.Uid, 64), Text(port.Name, 256), port.IsInput != 0));
            }
        }

        return ports;
    }

    /// <summary>Connects every input source (again: call it to pick up devices plugged in since).</summary>
    public static bool Open() => IsAvailable && OpenNative() == 0;

    public static void Close() {
        if (IsAvailable) {
            CloseNative();
        }
    }

    public static ulong NowNanoseconds() => NowNative();

    /// <summary>Drains queued input into <paramref name="into" />; returns the number of messages added.</summary>
    public static int Read(List<MidiMessage> into) {
        var buffer = stackalloc EventNative[ReadBatch];
        var total = 0;
        int read;
        while ((read = ReadNative(buffer, ReadBatch)) > 0) {
            for (var i = 0; i < read; i++) {
                ref var e = ref buffer[i];
                into.Add(new(e.TimeNs, e.Data[0], e.Data[1], e.Data[2], e.Length));
            }

            total += read;
        }

        return total;
    }

    /// <summary>Sends a short message to one destination (by uid) or to all of them (null).</summary>
    public static bool Send(string? destinationUid, ReadOnlySpan<byte> message) {
        fixed (byte* data = message) {
            return IsAvailable && SendNative(destinationUid, data, message.Length) == 0;
        }
    }

    static string Text(byte* text, int size) =>
        Marshal.PtrToStringUTF8((nint)text, Math.Max(0, IndexOfZero(text, size))) ?? string.Empty;

    static int IndexOfZero(byte* text, int size) {
        for (var i = 0; i < size; i++) {
            if (text[i] == 0) {
                return i;
            }
        }

        return size;
    }

    [LibraryImport(Library, EntryPoint = "cth_midi_port_count")]
    internal static partial int PortCount();

    [LibraryImport(Library, EntryPoint = "cth_midi_port_info")]
    internal static partial int PortInfo(int index, PortNative* port);

    [LibraryImport(Library, EntryPoint = "cth_midi_open")]
    internal static partial int OpenNative();

    [LibraryImport(Library, EntryPoint = "cth_midi_read")]
    internal static partial int ReadNative(EventNative* events, int max);

    [LibraryImport(Library, EntryPoint = "cth_midi_send", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SendNative(string? destinationUid, byte* data, int length);

    [LibraryImport(Library, EntryPoint = "cth_midi_close")]
    internal static partial void CloseNative();

    [LibraryImport(Library, EntryPoint = "cth_midi_now_ns")]
    internal static partial ulong NowNative();

    [StructLayout(LayoutKind.Sequential)]
    internal struct PortNative {
        public fixed byte Uid[64];
        public fixed byte Name[256];
        public int IsInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EventNative {
        public ulong TimeNs;
        public fixed byte Data[3];
        public byte Length;
        public fixed byte Reserved[4];
    }
}
