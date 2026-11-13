using System.Buffers.Binary;
using System.Text;

namespace FluentCMS.Core.Tests.Transfer;

// Changes what a zip's central directory says of one file, or the file's compressed bytes, a few bytes each. Shared with the
// transfer tests of FluentCMS.Data.Tests, which build crafted packages with it.
internal static class ZipCentralDirectory
{
    private const uint FileHeader = 0x02014b50;
    private const int NameAt = 46;

    // The uncompressed size declared.
    public static byte[] Declare(byte[] zip, string path, uint size) => Change(zip, path, header => BinaryPrimitives.WriteUInt32LittleEndian(header[24..], size));

    public static byte[] DeclareCompressed(byte[] zip, string path, uint size) => Change(zip, path, header => BinaryPrimitives.WriteUInt32LittleEndian(header[20..], size));

    public static byte[] Method(byte[] zip, string path, ushort method) => Change(zip, path, header => BinaryPrimitives.WriteUInt16LittleEndian(header[10..], method));

    // The compressed bytes of the file, each 0xFF, which a deflated file cannot be read with: its first block has the
    // reserved type.
    public static byte[] Corrupt(byte[] zip, string path)
    {
        var bytes = zip.ToArray();
        var header = bytes.AsSpan(Find(bytes, path));
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
        var local = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);
        var data = local + 30 + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local + 26)) + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local + 28));
        bytes.AsSpan(data, size).Fill(0xFF);
        return bytes;
    }

    private static byte[] Change(byte[] zip, string path, SpanAction change)
    {
        var bytes = zip.ToArray();
        change(bytes.AsSpan(Find(bytes, path)));
        return bytes;
    }

    private static int Find(byte[] bytes, string path)
    {
        var name = Encoding.UTF8.GetBytes(path);
        for (var at = bytes.Length - NameAt - name.Length; at >= 0; at--)
        {
            var header = bytes.AsSpan(at);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) == FileHeader
                && BinaryPrimitives.ReadUInt16LittleEndian(header[28..]) == name.Length
                && header.Slice(NameAt, name.Length).SequenceEqual(name))
            {
                return at;
            }
        }

        throw new ArgumentException($"The zip has no file {path}.", nameof(path));
    }

    private delegate void SpanAction(Span<byte> header);
}
