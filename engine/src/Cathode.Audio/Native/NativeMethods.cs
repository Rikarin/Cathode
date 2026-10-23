using System.Runtime.InteropServices;

namespace Cathode.Audio.Native;

/// <summary>P/Invoke surface of <c>cth_audio</c> (engine/native/cth_audio/include/cth_audio.h).</summary>
static unsafe partial class NativeMethods {
    internal const int TimingBins = 201; // CTH_TIMING_BINS
    const string Library = "cth_audio";

    [LibraryImport(Library, EntryPoint = "cth_audio_device_count")]
    internal static partial int DeviceCount();

    [LibraryImport(Library, EntryPoint = "cth_audio_device_info")]
    internal static partial int DeviceInfo(int index, DeviceInfoNative* info);

    [LibraryImport(Library, EntryPoint = "cth_audio_open", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Open(
        string? deviceUid,
        ref StreamConfigNative config,
        delegate* unmanaged<void*, float**, int, float**, int, int, void> process,
        void* user,
        out nint stream
    );

    [LibraryImport(Library, EntryPoint = "cth_audio_start")]
    internal static partial int Start(nint stream);

    [LibraryImport(Library, EntryPoint = "cth_audio_stop")]
    internal static partial int Stop(nint stream);

    [LibraryImport(Library, EntryPoint = "cth_audio_close")]
    internal static partial void Close(nint stream);

    [LibraryImport(Library, EntryPoint = "cth_audio_stats")]
    internal static partial void Stats(nint stream, out StreamStatsNative stats);

    [LibraryImport(Library, EntryPoint = "cth_audio_timing")]
    internal static partial void Timing(nint stream, TimingNative* timing);

    [LibraryImport(Library, EntryPoint = "cth_audio_reset_timing")]
    internal static partial void ResetTiming(nint stream);

    [LibraryImport(Library, EntryPoint = "cth_audio_backend")]
    internal static partial byte* Backend();

    [LibraryImport(Library, EntryPoint = "cth_audio_last_error")]
    internal static partial byte* LastError();

    [LibraryImport(Library, EntryPoint = "cth_fpu_flush_denormals")]
    internal static partial void FlushDenormals();

    internal static string LastErrorMessage() => Marshal.PtrToStringUTF8((nint)LastError()) ?? "unknown error";

    [StructLayout(LayoutKind.Sequential)]
    internal struct StreamConfigNative {
        public double SampleRate;
        public int BufferFrames;
        public int InputChannels;
        public int OutputChannels;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StreamStatsNative {
        public ulong Callbacks;
        public ulong Overloads;
        public int InputLatencyFrames;
        public int OutputLatencyFrames;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TimingNative {
        public fixed ulong Bins[TimingBins];
        public ulong OverBudget;
        public double MaxPercent;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfoNative {
        public fixed byte Uid[256];
        public fixed byte Name[256];
        public int InputChannels;
        public int OutputChannels;
        public double SampleRate;
    }
}
