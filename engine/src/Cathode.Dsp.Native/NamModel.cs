using Cathode.Dsp.Capture;
using System.Runtime.InteropServices;

namespace Cathode.Dsp.Native;

/// <summary>Metadata of a loaded .nam model.</summary>
public sealed record NamModelInfo(
    string Architecture,
    string FileVersion,
    double ExpectedSampleRate,
    double? Loudness,
    double? InputLevel,
    double? OutputLevel,
    int PrewarmSamples,
    bool Slimmable
);

/// <summary>
///     A Neural Amp Modeler model hosted by the reference C++ core (NeuralAmpModelerCore) through <c>cth_nam</c>.
///     Load, <see cref="Reset" /> and <see cref="SetSize" /> run on the control thread; <see cref="Process" /> is
///     real-time safe.
/// </summary>
public sealed unsafe class NamModel : ICaptureModel {
    nint handle;

    public NamModelInfo Info { get; }

    public string Format => "NAM";

    public string Architecture => Info.Architecture;

    public double ExpectedSampleRate => Info.ExpectedSampleRate;

    public double? Loudness => Info.Loudness;

    /// <summary>Version of the bundled NeuralAmpModelerCore.</summary>
    public static string CoreVersion => Marshal.PtrToStringUTF8((nint)NamNative.CoreVersion()) ?? "unknown";

    /// <summary>True when the native library was built (engine/native/build.sh).</summary>
    public static bool IsLibraryPresent {
        get {
            try {
                _ = NamNative.CoreVersion();
                return true;
            } catch (DllNotFoundException) {
                return false;
            }
        }
    }

    nint Handle => handle != 0 ? handle : throw new ObjectDisposedException(nameof(NamModel));

    NamModel(nint handle, NamModelInfo info) {
        this.handle = handle;
        Info = info;
    }

    /// <summary>
    ///     Process-global: use the fast tanh approximation in models loaded afterwards (and in LSTM processing).
    ///     The NAM plugin enables it; it is roughly 1.5x faster for WaveNet models.
    /// </summary>
    public static void SetFastTanh(bool enabled) => NamNative.SetFastTanh(enabled ? 1 : 0);

    public static NamModel Load(string path) {
        if (NamNative.Load(path, out var handle) != 0) {
            throw new InvalidDataException($"Could not load NAM model '{path}': {NamNative.LastErrorMessage()}");
        }

        NamNative.GetInfo(handle, out var raw);
        var info = new NamModelInfo(
            Marshal.PtrToStringUTF8((nint)raw.Architecture) ?? string.Empty,
            Marshal.PtrToStringUTF8((nint)raw.Version) ?? string.Empty,
            raw.ExpectedSampleRate,
            raw.HasLoudness != 0 ? raw.Loudness : null,
            raw.HasInputLevel != 0 ? raw.InputLevel : null,
            raw.HasOutputLevel != 0 ? raw.OutputLevel : null,
            raw.PrewarmSamples,
            raw.Slimmable != 0
        );
        return new(handle, info);
    }

    /// <summary>Control thread: allocate for blocks of up to <paramref name="maxFrames" /> and pre-warm.</summary>
    public void Reset(double sampleRate, int maxFrames) {
        if (NamNative.Reset(Handle, sampleRate, maxFrames) != 0) {
            throw new InvalidOperationException($"NAM reset failed: {NamNative.LastErrorMessage()}");
        }
    }

    /// <summary>Control thread, slimmable models only: 0 = smallest (Lite), 1 = full size. Re-warms the model.</summary>
    public void SetSize(double size) {
        if (NamNative.SetSize(Handle, size) != 0) {
            throw new InvalidOperationException($"NAM resize failed: {NamNative.LastErrorMessage()}");
        }
    }

    /// <summary>Audio thread. <paramref name="input" /> and <paramref name="output" /> may be the same span.</summary>
#pragma warning disable SK2142 // False positive: `fixed` pins the spans and reads them; it does not overwrite the parameters.
    public void Process(ReadOnlySpan<float> input, Span<float> output) {
#pragma warning restore SK2142
        fixed (float* i = input) {
            fixed (float* o = output) {
                NamNative.Process(handle, i, o, output.Length);
            }
        }
    }

    public void Dispose() {
        if (handle != 0) {
            NamNative.Free(handle);
            handle = 0;
        }
    }
}
