using System.Runtime.InteropServices;

namespace Cathode.Dsp.Tests;

/// <summary>
///     Where the exact render hashes (<see cref="AmpDefaultsTests" />, <see cref="InverterBitIdentityTests" />) hold:
///     they were made on macOS ARM64, and .NET's maths functions (exp, tanh, pow) may differ in the last bit on another
///     OS or CPU, so a hash taken there says nothing about a change. The macOS CI runner is ARM64, so every push still
///     runs them; elsewhere they are skipped with this reason.
/// </summary>
static class BaselinePlatform {
    public static void Require() =>
        Assert.SkipUnless(
            OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            "Bit-identity baselines are macOS ARM64 renders; other platforms' floating point differs in the last bit."
        );
}
