using System.Runtime.InteropServices;
using static Cathode.Audio.Native.NativeMethods;

namespace Cathode.Audio.Native;

public sealed record AudioDeviceInfo(string Uid, string Name, int InputChannels, int OutputChannels, double SampleRate);

/// <summary>Entry points of the native audio shim that are not tied to a stream.</summary>
public static unsafe class NativeAudio {
    /// <summary>True when the native library was built and can be loaded (see native/build.sh).</summary>
    public static bool IsLibraryPresent {
        get {
            try {
                _ = Backend();
                return true;
            } catch (DllNotFoundException) {
                return false;
            }
        }
    }

    /// <summary>True when the native library is present and has a device backend for this platform.</summary>
    public static bool IsAvailable => IsLibraryPresent && Backend != "none";

    /// <summary>"coreaudio", "wasapi", or "none" on platforms whose backend isn't written yet.</summary>
    public static string Backend => Marshal.PtrToStringUTF8((nint)Backend()) ?? "none";

    public static IReadOnlyList<AudioDeviceInfo> Devices() {
        var count = DeviceCount();
        var devices = new List<AudioDeviceInfo>(count);
        for (var i = 0; i < count; i++) {
            DeviceInfoNative info;
            if (DeviceInfo(i, &info) == 0) {
                devices.Add(
                    new(
                        Marshal.PtrToStringUTF8((nint)info.Uid) ?? string.Empty,
                        Marshal.PtrToStringUTF8((nint)info.Name) ?? string.Empty,
                        info.InputChannels,
                        info.OutputChannels,
                        info.SampleRate
                    )
                );
            }
        }

        return devices;
    }

    /// <summary>Enables flush-to-zero / denormals-are-zero on the calling thread.</summary>
    public static void FlushDenormalsOnCurrentThread() => FlushDenormals();
}
