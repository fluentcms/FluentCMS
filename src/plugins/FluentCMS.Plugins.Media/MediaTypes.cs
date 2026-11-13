using System.Text;

namespace FluentCMS.Plugins.Media;

internal sealed record MediaType(string MimeType, string Extension, bool IsImage);

// The types the plugin recognises by their first bytes, never by a name or a Content-Type. An SVG is text, so its first
// bytes only make it a candidate, which SvgDocument settles by parsing it.
internal static class MediaTypes
{
    // Enough for every signature here, the EBML header of WebM included.
    public const int HeaderLength = 64;

    public static readonly MediaType Jpeg = new("image/jpeg", ".jpg", true);
    public static readonly MediaType Png = new("image/png", ".png", true);
    public static readonly MediaType Gif = new("image/gif", ".gif", true);
    public static readonly MediaType WebP = new("image/webp", ".webp", true);
    public static readonly MediaType Avif = new("image/avif", ".avif", true);
    public static readonly MediaType Svg = new("image/svg+xml", ".svg", true);
    public static readonly MediaType Pdf = new("application/pdf", ".pdf", false);
    public static readonly MediaType Mp4 = new("video/mp4", ".mp4", false);
    public static readonly MediaType WebM = new("video/webm", ".webm", false);
    public static readonly MediaType Mp3 = new("audio/mpeg", ".mp3", false);
    public static readonly MediaType Wav = new("audio/wav", ".wav", false);

    // What a site's allow-list may hold. SVG is governed by its own setting.
    public static readonly IReadOnlyList<MediaType> Allowable = [Jpeg, Png, Gif, WebP, Avif, Pdf, Mp4, WebM, Mp3, Wav];

    private static readonly IReadOnlyList<MediaType> Known = [.. Allowable, Svg];

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".scr", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".cpl", ".sh", ".bash", ".jar", ".apk", ".app", ".dmg", ".so", ".dylib", ".elf",
    };

    private static readonly string[] Mp4Brands = ["isom", "iso2", "iso4", "iso5", "iso6", "mp41", "mp42", "avc1", "dash", "M4V "];

    public static MediaType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return Jpeg;
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }

        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8))
        {
            return Gif;
        }

        if (header.Length >= 12 && header.StartsWith("RIFF"u8))
        {
            var form = header[8..12];
            return form.SequenceEqual("WEBP"u8) ? WebP : form.SequenceEqual("WAVE"u8) ? Wav : null;
        }

        if (header.StartsWith("%PDF-"u8))
        {
            return Pdf;
        }

        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(header[8..12]);
            return brand is "avif" or "avis" ? Avif : Mp4Brands.Contains(brand, StringComparer.Ordinal) ? Mp4 : null;
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
        {
            return header.IndexOf("webm"u8) > 0 ? WebM : null;
        }

        // An ID3 tag, or the sync word of an MPEG audio frame of layer III.
        if (header.StartsWith("ID3"u8) || (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0 && (header[1] & 0x06) == 0x02))
        {
            return Mp3;
        }

        return null;
    }

    // Windows PE, ELF, Mach-O (both byte orders, 32 and 64 bits, and universal binaries, whose magic Java class files share)
    // and a script with a shebang line, with or without a byte order mark before it.
    public static bool IsExecutable(ReadOnlySpan<byte> header)
    {
        var text = header.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? header[3..] : header;
        return header.StartsWith("MZ"u8)
            || header.StartsWith((ReadOnlySpan<byte>)[0x7F, 0x45, 0x4C, 0x46])
            || header.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xED, 0xFA, 0xCE])
            || header.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xED, 0xFA, 0xCF])
            || header.StartsWith((ReadOnlySpan<byte>)[0xCE, 0xFA, 0xED, 0xFE])
            || header.StartsWith((ReadOnlySpan<byte>)[0xCF, 0xFA, 0xED, 0xFE])
            || header.StartsWith((ReadOnlySpan<byte>)[0xCA, 0xFE, 0xBA, 0xBE])
            || text.StartsWith("#!"u8);
    }

    public static bool HasExecutableName(string fileName) => ExecutableExtensions.Contains(Path.GetExtension(fileName));

    // The type a stored MIME type names, without regard to case, or null when it is none the plugin finds from bytes.
    public static MediaType? Find(string mimeType) =>
        Known.FirstOrDefault(type => string.Equals(type.MimeType, mimeType, StringComparison.OrdinalIgnoreCase));

    // Text that starts with markup, after a byte order mark and white space.
    public static bool MayBeSvg(ReadOnlySpan<byte> header)
    {
        var text = header.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? header[3..] : header;
        var start = text.IndexOfAnyExcept((ReadOnlySpan<byte>)[(byte)' ', (byte)'\t', (byte)'\r', (byte)'\n']);
        return start >= 0 && text[start] == (byte)'<';
    }
}
