using System.Buffers.Binary;

namespace FluentCMS.Core.Transfer;

// The number of files that the end of a zip's central directory declares, found as System.IO.Compression finds it: the
// record nearest the end of the file, and the Zip64 record its locator points at when a field of it is at its maximum.
// ZipArchive reads the central directory whole when the entries are first asked for and stops once it holds more than this
// number, so the import refuses a package that declares more files than it takes before ZipArchive holds an object for each.
internal static class PackageDirectory
{
    private const uint EndSignature = 0x06054b50;
    private const uint LocatorSignature = 0x07064b50;
    private const uint Zip64EndSignature = 0x06064b50;
    private const int EndSize = 22;
    private const int LocatorSize = 20;
    private const int Zip64EndSize = 56;
    private const int MaxCommentLength = ushort.MaxValue;

    // Null when the file has no end record that can be read.
    public static async Task<long?> DeclaredEntriesAsync(Stream file, CancellationToken cancellationToken)
    {
        var length = file.Length;
        if (length < EndSize)
        {
            return null;
        }

        var tailLength = (int)Math.Min(length, EndSize + MaxCommentLength + LocatorSize);
        var tail = new byte[tailLength];
        var tailStart = length - tailLength;
        file.Position = tailStart;
        await file.ReadExactlyAsync(tail, cancellationToken);

        var end = -1;
        for (var at = tailLength - EndSize; at >= Math.Max(0, tailLength - EndSize - MaxCommentLength); at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(at)) == EndSignature)
            {
                end = at;
                break;
            }
        }

        if (end < 0)
        {
            return null;
        }

        var record = tail.AsSpan(end);
        var disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        var directory = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        if (disk != ushort.MaxValue && entries != ushort.MaxValue && directory != uint.MaxValue)
        {
            return entries;
        }

        if (end < LocatorSize || BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end - LocatorSize)) != LocatorSignature)
        {
            return tailStart + end < LocatorSize ? null : entries;
        }

        var zip64End = BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(end - LocatorSize + 8));
        if (length < Zip64EndSize || zip64End > (ulong)(length - Zip64EndSize))
        {
            return null;
        }

        var zip64 = new byte[Zip64EndSize];
        file.Position = (long)zip64End;
        await file.ReadExactlyAsync(zip64, cancellationToken);
        if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != Zip64EndSignature)
        {
            return null;
        }

        var total = BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32));
        return total > long.MaxValue ? null : (long)total;
    }
}
