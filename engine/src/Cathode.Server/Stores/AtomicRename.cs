using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Cathode.Server.Stores;

/// <summary>
///     Renames a finished temporary file over its destination. On Windows a plain rename (<see cref="File.Move(string, string, bool)" />)
///     is refused while anyone has the destination open, even a reader who shares delete; a rename with POSIX semantics
///     (Windows 10 1809 and later, NTFS) replaces it anyway, and the reader keeps the old content. Elsewhere, and where
///     POSIX semantics are not supported, it is <see cref="File.Move(string, string, bool)" />.
/// </summary>
static partial class AtomicRename {
    const uint Delete = 0x0001_0000;
    const uint Synchronize = 0x0010_0000;
    const uint ShareAll = 0x7; // read, write, delete
    const uint OpenExisting = 3;
    const int FileRenameInfoEx = 22;
    const uint ReplaceIfExists = 0x1;
    const uint PosixSemantics = 0x2;
    const int ErrorInvalidParameter = 87;
    const int ErrorNotSupported = 50;

    public static void Replace(string source, string destination) {
        if (!OperatingSystem.IsWindows() || !TryPosixRename(Path.GetFullPath(source), Path.GetFullPath(destination))) {
            File.Move(source, destination, true);
        }
    }

    /// <summary>False when the file system does not take POSIX semantics (FAT, network shares, older Windows).</summary>
    static unsafe bool TryPosixRename(string source, string destination) {
        using var handle = CreateFileW(source, Delete | Synchronize, ShareAll, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid) {
            // An IOException, so the store retries it: a scanner or indexer that opened the fresh temporary file without
            // delete sharing refuses this open for a moment, as it would a plain rename.
            var openError = Marshal.GetLastPInvokeError();
            throw new IOException($"Could not open '{source}' to rename it.", new Win32Exception(openError));
        }

        // FILE_RENAME_INFO: Flags (4 bytes, padded to 8), RootDirectory (a handle), FileNameLength, then the name.
        var nameBytes = Encoding.Unicode.GetByteCount(destination);
        var nameOffset = (int)Marshal.OffsetOf<RenameInfo>(nameof(RenameInfo.FileName));
        var size = nameOffset + nameBytes + sizeof(char);
        var buffer = stackalloc byte[size];
        new Span<byte>(buffer, size).Clear();
        var info = (RenameInfo*)buffer;
        info->Flags = ReplaceIfExists | PosixSemantics;
        info->FileNameLength = (uint)nameBytes;
        Encoding.Unicode.GetBytes(destination, new Span<byte>(buffer + nameOffset, nameBytes));

        if (SetFileInformationByHandle(handle, FileRenameInfoEx, buffer, (uint)size)) {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error is ErrorInvalidParameter or ErrorNotSupported) {
            return false;
        }

        throw new UnauthorizedAccessException($"Could not replace '{destination}'.", new Win32Exception(error));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RenameInfo {
        public uint Flags;
        public nint RootDirectory;
        public uint FileNameLength;
        public char FileName;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string name,
        uint access,
        uint share,
        nint security,
        uint disposition,
        uint flags,
        nint template
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, void* info, uint size);
}
