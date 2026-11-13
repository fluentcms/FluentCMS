using System.Globalization;
using System.IO.Compression;
using FluentCMS.Content;

namespace FluentCMS.Core.Transfer;

// What one import may read of its package (spec 4.14). Each file it opens counts its compressed size against
// FluentCms:Transfer:MaxReadSize and its declared size against MaxUnpackedSize, whatever it then reads of it, so a file
// opened again costs again. The first refusal stays: every later open, and every report of a contributor, throws it
// again, so that the import stops even when a contributor catches it.
internal sealed class PackageBudget(TransferOptions options)
{
    private long _compressed;
    private long _unpacked;

    public ValidationException? Refusal { get; private set; }

    // The file the refusal is about.
    public string? RefusedPath { get; private set; }

    public void Open(ZipArchiveEntry entry)
    {
        ThrowIfRefused();
        _compressed += entry.CompressedLength;
        _unpacked += entry.Length;
        if (_compressed > options.ReadLimit)
        {
            throw Refuse(entry.FullName, $"The import read more than {options.ReadLimit.ToString(CultureInfo.InvariantCulture)} compressed bytes of the package when it opened {entry.FullName}, FluentCms:Transfer:MaxReadSize.");
        }

        if (_unpacked > options.UnpackedLimit)
        {
            throw Refuse(entry.FullName, $"The import opened more than {options.UnpackedLimit.ToString(CultureInfo.InvariantCulture)} bytes of the package unpacked when it opened {entry.FullName}, FluentCms:Transfer:MaxUnpackedSize.");
        }
    }

    // The longest line of a file read as lines, or null for any other file.
    public int? LineLimit(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith(TransferPackage.LinesExtension, StringComparison.Ordinal) ? options.MaxLineLength : null;

    public ValidationException Refuse(string path, string message)
    {
        if (Refusal is null)
        {
            Refusal = TransferImporter.Invalid(message);
            RefusedPath = path;
        }

        return Refusal;
    }

    public void ThrowIfRefused()
    {
        if (Refusal is { } refusal)
        {
            throw refusal;
        }
    }
}
