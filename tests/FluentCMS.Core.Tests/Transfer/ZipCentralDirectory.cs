using System.Buffers.Binary;
using System.Text;

namespace FluentCMS.Core.Tests.Transfer;

// Sets the uncompressed size that a zip's central directory declares for one file, a change of four bytes. Shared with the
// transfer tests of FluentCMS.Data.Tests, which build crafted packages with it.
internal static class ZipCentralDirectory
{
    private const uint FileHeader = 0x02014b50;
    private const int NameAt = 46;

    public static byte[] Declare(byte[] zip, string path, uint size)
    {
        var name = Encoding.UTF8.GetBytes(path);
        var bytes = zip.ToArray();
        for (var at = bytes.Length - NameAt - name.Length; at >= 0; at--)
        {
            var header = bytes.AsSpan(at);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) == FileHeader
                && BinaryPrimitives.ReadUInt16LittleEndian(header[28..]) == name.Length
                && header.Slice(NameAt, name.Length).SequenceEqual(name))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(header[24..], size);
                return bytes;
            }
        }

        throw new ArgumentException($"The zip has no file {path}.", nameof(path));
    }
}
