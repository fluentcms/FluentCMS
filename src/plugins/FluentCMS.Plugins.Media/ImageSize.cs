using System.Buffers.Binary;

namespace FluentCMS.Plugins.Media;

// The width and height that an image states in its headers, for JPEG, PNG, GIF and WebP. Null when the headers do not
// state them where the format puts them; the upload goes on without them.
internal static class ImageSize
{
    // A JPEG may put its frame header after many segments of metadata; this many is far beyond what an encoder writes.
    private const int MaxJpegSegments = 1024;

    public static async Task<(int Width, int Height)?> ReadAsync(Stream stream, MediaType type, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var header = new byte[32];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var bytes = header.AsSpan(0, read);

        (int Width, int Height)? size = type == MediaTypes.Png ? Png(bytes)
            : type == MediaTypes.Gif ? Gif(bytes)
            : type == MediaTypes.WebP ? WebP(bytes)
            : type == MediaTypes.Jpeg ? await JpegAsync(stream, cancellationToken)
            : null;
        return size is { Width: > 0, Height: > 0 } ? size : null;
    }

    // The IHDR chunk comes first.
    private static (int, int)? Png(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 24 && bytes[12..16].SequenceEqual("IHDR"u8)
            ? (Int(BinaryPrimitives.ReadUInt32BigEndian(bytes[16..])), Int(BinaryPrimitives.ReadUInt32BigEndian(bytes[20..])))
            : null;

    // The logical screen.
    private static (int, int)? Gif(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 10 ? (BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..])) : null;

    private static (int, int)? WebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30)
        {
            return null;
        }

        var chunk = bytes[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            return (BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8) && bytes[20] == 0x2F)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            return (1 + (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)), 1 + (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)));
        }

        return null;
    }

    // Walks the segments to the first start of frame, skipping each by its length.
    private static async Task<(int, int)?> JpegAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[9];
        stream.Position = 2;
        for (var segment = 0; segment < MaxJpegSegments; segment++)
        {
            if (await stream.ReadAtLeastAsync(buffer.AsMemory(0, 2), 2, throwOnEndOfStream: false, cancellationToken) < 2 || buffer[0] != 0xFF)
            {
                return null;
            }

            var marker = buffer[1];
            while (marker == 0xFF)
            {
                if (await stream.ReadAtLeastAsync(buffer.AsMemory(1, 1), 1, throwOnEndOfStream: false, cancellationToken) < 1)
                {
                    return null;
                }

                marker = buffer[1];
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return null;
            }

            if (await stream.ReadAtLeastAsync(buffer.AsMemory(0, 2), 2, throwOnEndOfStream: false, cancellationToken) < 2)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(buffer);
            if (length < 2)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (await stream.ReadAtLeastAsync(buffer.AsMemory(0, 5), 5, throwOnEndOfStream: false, cancellationToken) < 5)
                {
                    return null;
                }

                return (BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(3)), BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(1)));
            }

            stream.Seek(length - 2, SeekOrigin.Current);
        }

        return null;
    }

    private static int Int(uint value) => value > int.MaxValue ? 0 : (int)value;
}
