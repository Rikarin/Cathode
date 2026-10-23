using System.Runtime.InteropServices;

namespace Cathode.Dsp.Native;

/// <summary>P/Invoke surface of <c>cth_nam</c> (engine/native/cth_nam/include/cth_nam.h).</summary>
internal static unsafe partial class NamNative
{
    private const string Library = "cth_nam";

    [LibraryImport(Library, EntryPoint = "cth_nam_load", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Load(string path, out nint model);

    [LibraryImport(Library, EntryPoint = "cth_nam_get_info")]
    internal static partial void GetInfo(nint model, out Info info);

    [LibraryImport(Library, EntryPoint = "cth_nam_reset")]
    internal static partial int Reset(nint model, double sampleRate, int maxFrames);

    [LibraryImport(Library, EntryPoint = "cth_nam_set_size")]
    internal static partial int SetSize(nint model, double size);

    /// <summary>
    /// Real-time call. Deliberately not <c>SuppressGCTransition</c>: a model takes long enough that the GC must be
    /// able to proceed while it runs (docs/02-architecture.md §5.7).
    /// </summary>
    [LibraryImport(Library, EntryPoint = "cth_nam_process")]
    internal static partial void Process(nint model, float* input, float* output, int frames);

    [LibraryImport(Library, EntryPoint = "cth_nam_free")]
    internal static partial void Free(nint model);

    [LibraryImport(Library, EntryPoint = "cth_nam_set_fast_tanh")]
    internal static partial void SetFastTanh(int enabled);

    [LibraryImport(Library, EntryPoint = "cth_nam_core_version")]
    internal static partial byte* CoreVersion();

    [LibraryImport(Library, EntryPoint = "cth_nam_last_error")]
    internal static partial byte* LastError();

    internal static string LastErrorMessage() => Marshal.PtrToStringUTF8((nint)LastError()) ?? "unknown error";

    [StructLayout(LayoutKind.Sequential)]
    internal struct Info
    {
        public fixed byte Architecture[64];
        public fixed byte Version[32];
        public double ExpectedSampleRate;
        public int HasLoudness;
        public double Loudness;
        public int HasInputLevel;
        public double InputLevel;
        public int HasOutputLevel;
        public double OutputLevel;
        public int PrewarmSamples;
        public int Slimmable;
    }
}
