using System.Reflection.PortableExecutable;

namespace Cathode.Engine.Tests;

/// <summary>
///     The Windows shims carry their C and C++ runtimes (engine/native/CMakeLists.txt, CMAKE_MSVC_RUNTIME_LIBRARY).
///     Windows has no MSVCP140.dll or VCRUNTIME140.dll of its own and the installer brings no Visual C++
///     Redistributable, so a shim that imports them fails to load on a clean machine.
/// </summary>
public class WindowsShimRuntimeTests {
    static readonly string[] RuntimePrefixes = ["msvcp", "vcruntime", "concrt", "vcomp", "ucrtbased"];

    [Theory]
    [InlineData("cth_audio.dll")]
    [InlineData("cth_nam.dll")]
    public void A_shim_imports_no_Visual_Cpp_runtime_library(string shim) {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "checks the Windows shims");
        var path = Path.Combine(AppContext.BaseDirectory, shim);
        Assert.SkipUnless(File.Exists(path), "native shim not built (run native/build.sh)");

        var imports = ImportedLibraries(path);

        Assert.Contains(imports, static name => name.Equals("KERNEL32.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(imports, IsVisualCppRuntime);
    }

    [Theory]
    [InlineData("MSVCP140.dll", true)]
    [InlineData("msvcp140_1.dll", true)]
    [InlineData("VCRUNTIME140.dll", true)]
    [InlineData("VCRUNTIME140_1.dll", true)]
    [InlineData("VCRUNTIME140D.dll", true)]
    [InlineData("CONCRT140.dll", true)]
    [InlineData("VCOMP140.DLL", true)]
    [InlineData("ucrtbased.dll", true)]
    [InlineData("KERNEL32.dll", false)]
    [InlineData("ole32.dll", false)]
    [InlineData("api-ms-win-crt-runtime-l1-1-0.dll", false)] // the Universal CRT is part of Windows 10 and later
    public void The_Visual_Cpp_runtime_libraries_are_told_from_Windows_own(string library, bool redistributable) =>
        Assert.Equal(redistributable, IsVisualCppRuntime(library));

    /// <summary>The libraries the Visual C++ Redistributable (or only Visual Studio, for debug ones) installs.</summary>
    static bool IsVisualCppRuntime(string library) =>
        RuntimePrefixes.Any(prefix => library.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The DLL names in a PE file's import table.</summary>
    static List<string> ImportedLibraries(string path) {
        using var pe = new PEReader(File.OpenRead(path));
        var names = new List<string>();
        var table = pe.PEHeaders.PEHeader!.ImportTableDirectory;
        if (table.Size == 0) {
            return names;
        }

        // IMAGE_IMPORT_DESCRIPTOR: five 32-bit fields, the fourth the address of the library's name; a zero one ends
        // the table.
        var descriptors = pe.GetSectionData(table.RelativeVirtualAddress).GetReader();
        while (descriptors.RemainingBytes >= 20) {
            descriptors.Offset += 12;
            var nameAddress = descriptors.ReadInt32();
            descriptors.Offset += 4;
            if (nameAddress == 0) {
                break;
            }

            var name = pe.GetSectionData(nameAddress).GetReader();
            names.Add(name.ReadUTF8(name.IndexOf(0)));
        }

        return names;
    }
}
