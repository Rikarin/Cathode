using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Cathode.Server.Stores;

/// <summary>
///     Renames a finished temporary file over its destination. On Windows a plain rename
///     (<see cref="File.Move(string, string, bool)" />)
///     is refused while anyone has the destination open, even a reader who shares delete; a rename with POSIX semantics
///     (Windows 10 1809 and later, NTFS) replaces it anyway, and the reader keeps the old content. Elsewhere, and where
///     POSIX semantics are not supported, it is <see cref="File.Move(string, string, bool)" />. <see cref="Write" /> is
///     the whole durable save the stores make with it.
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

    /// <summary>
    ///     Replaces <paramref name="path" /> with <paramref name="contents" /> (UTF-8): written to a temporary file of
    ///     its own beside it (two writers of one file never share one), flushed to the disk, renamed over the file, and
    ///     on Linux and macOS the folder flushed too, so after a power cut the file is the old one or the new one,
    ///     whole. A sharing violation or denied access (an indexer or a scanner holding a file a moment) is retried
    ///     briefly.
    /// </summary>
    public static void Write(string path, string contents) {
        var bytes = Encoding.UTF8.GetBytes(contents);
        Retrying(() => {
                var temporary = $"{path}.{Guid.NewGuid():n}.tmp";
                try {
                    using (var stream = new FileStream(
                               temporary,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None
                           )) {
                        stream.Write(bytes);
                        stream.Flush(true);
                    }

                    Replace(temporary, path);
                } catch (Exception) {
                    TryDelete(temporary);
                    throw;
                }

                FlushFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);
                return true;
            }
        );
    }

    /// <summary>
    ///     Runs a file operation, retrying a sharing violation or a denied access for up to about a
    ///     second.
    /// </summary>
    public static TResult Retrying<TResult>(Func<TResult> operation) {
        for (var attempt = 1;; attempt++) {
            try {
                return operation();
            } catch (Exception e) when (e is IOException
                                            and not FileNotFoundException
                                            and not DirectoryNotFoundException
                                            or UnauthorizedAccessException
                                        && attempt < 20) {
                Thread.Sleep(5 * attempt);
            }
        }
    }

    static void TryDelete(string path) {
        try {
            File.Delete(path);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            // a temporary file left behind; the save's own error is the one to report
        }
    }

    /// <summary>
    ///     Makes a rename in <paramref name="folder" /> durable where the file system needs the folder flushed for it
    ///     (POSIX); best effort. NTFS journals the rename itself.
    /// </summary>
    static void FlushFolder(string folder) {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var descriptor = open(folder, 0); // O_RDONLY
        if (descriptor < 0) {
            return;
        }

        _ = fsync(descriptor);
        _ = close(descriptor);
    }

    public static void Replace(string source, string destination) {
        if (!OperatingSystem.IsWindows() || !TryPosixRename(Path.GetFullPath(source), Path.GetFullPath(destination))) {
            File.Move(source, destination, true);
        }
    }

    /// <summary>
    ///     False when the file system does not take POSIX semantics (FAT, network shares, older Windows).
    /// </summary>
    static unsafe bool TryPosixRename(string source, string destination) {
        using var handle = CreateFileW(source, Delete | Synchronize, ShareAll, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid) {
            // An IOException, so the store retries it: a scanner or indexer that opened the fresh temporary file
            // without delete sharing refuses this open for a moment, as it would a plain rename.
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
        Encoding.Unicode.GetBytes(destination, new(buffer + nameOffset, nameBytes));

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

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int open(string path, int flags);

    [LibraryImport("libc")]
    private static partial int fsync(int descriptor);

    [LibraryImport("libc")]
    private static partial int close(int descriptor);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformationByHandle(
        SafeFileHandle file,
        int infoClass,
        void* info,
        uint size
    );
}
