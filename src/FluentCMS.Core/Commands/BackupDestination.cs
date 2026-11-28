using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FluentCMS.Core.Commands;

internal static class BackupDestination
{
    private static readonly string[] JournalSuffixes = ["-wal", "-shm", "-journal"];

    public static void Check(string output, string database, DirectoryInfo? media)
    {
        var destinations = Names(output);
        var databaseNames = Names(database);
        var journals = databaseNames.SelectMany(name => JournalSuffixes.SelectMany(suffix => Names(name + suffix)));
        if (databaseNames.Concat(journals).Any(input => destinations.Any(path => CouldMatch(input, path, descendants: false)))
            || (media is not null && Names(media.FullName).Any(root => destinations.Any(path => CouldMatch(root, path, descendants: true)))))
        {
            throw new CommandException(TransferCommandErrorCodes.UnsafeBackupDestination,
                $"{output} could name the database, a journal file or a path in the local media storage. Choose an archive outside those paths.");
        }
    }

    private static bool CouldMatch(string input, string output, bool descendants)
    {
        var left = Path.GetPathRoot(input)!;
        var right = Path.GetPathRoot(output)!;
        var a = input[left.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var b = output[right.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (b.Length < a.Length || (!descendants && b.Length != a.Length) || DifferentAsciiNames(left, right))
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            left = Path.Combine(left, a[i]);
            right = Path.Combine(right, b[i]);
            if (!a[i].Equals(b[i], StringComparison.OrdinalIgnoreCase)
                && (DifferentAsciiNames(a[i], b[i]) || KnownDistinctEntries(left, right)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DifferentAsciiNames(string left, string right) =>
        Ascii.IsValid(left) && Ascii.IsValid(right) && !left.Equals(right, StringComparison.OrdinalIgnoreCase);

    // Filesystems can fold a non-ASCII character to multiple letters. Don't guess that mapping, especially for
    // future journal names. If both lookups succeed, only two actual directory entries establish distinct spellings.
    private static bool KnownDistinctEntries(string input, string output)
    {
        if (CanonicalPath(output) is null)
        {
            return false;
        }

        if (CanonicalPath(input) is null)
        {
            return true;
        }

        var inputName = Path.GetFileName(input);
        var outputName = Path.GetFileName(output);
        var sawInput = false;
        var sawOutput = false;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(output)!))
            {
                var name = Path.GetFileName(entry);
                sawInput |= name == inputName;
                sawOutput |= name == outputName;
                if (sawInput && sawOutput)
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Uncertain(output);
        }

        return false;
    }

    // Keep the placement of the move and each existing ancestor's physical placement. A missing leaf takes its
    // parent's placement. Compare conservatively even on case-sensitive mounts; a case-only sibling may be refused.
    // This is a path guard in a stable, trusted mount namespace, not a file-ID check across alternate mounts or shares.
    private static HashSet<string> Names(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var names = new HashSet<string>(StringComparer.Ordinal) { path };
        var found = false;
        for (string? ancestor = path; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
        {
            var canonical = CanonicalPath(ancestor);
            if (canonical is null)
            {
                if (new FileInfo(ancestor).LinkTarget is not null)
                {
                    throw Uncertain(ancestor);
                }

                continue;
            }

            found = true;
            names.Add(Path.TrimEndingDirectorySeparator(Path.Join(canonical, path.AsSpan(ancestor.Length).TrimStart(Path.DirectorySeparatorChar))));
        }

        return found ? names : throw Uncertain(path);
    }

    private static CommandException Uncertain(string path) => new(TransferCommandErrorCodes.UnsafeBackupDestination,
        $"Cannot establish the location of '{path}' to check the backup destination. Choose an unambiguous path.");

    private static string? CanonicalPath(string path) => OperatingSystem.IsWindows() ? WindowsPath(path) : UnixPath(path);

    private static string? UnixPath(string path)
    {
        var resolved = RealPath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            return Marshal.GetLastPInvokeError() == 2 ? null : throw Uncertain(path); // ENOENT
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved)!;
        }
        finally
        {
            Free(resolved);
        }
    }

    private static string? WindowsPath(string path)
    {
        // Metadata access only, including for directories; SQLite is never opened by the guard.
        using var handle = CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return Marshal.GetLastPInvokeError() is 2 or 3 ? null : throw Uncertain(path); // FILE/PATH_NOT_FOUND
        }

        var buffer = new char[512];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length >= buffer.Length)
        {
            buffer = new char[length];
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        }

        return length > 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : throw Uncertain(path);
    }

    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
#pragma warning disable CA2101 // POSIX realpath takes the UTF-8 bytes of the path, not a UTF-16 string.
    private static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buffer);
#pragma warning restore CA2101

    [DllImport("libc", EntryPoint = "free")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void Free(IntPtr pointer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] buffer, uint size, uint flags);
}
