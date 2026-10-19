namespace FluentCMS.Core.Media;

// The media types an upload may declare (MED-01). Types a browser runs as a document, such as text/html, application/xhtml+xml
// and image/svg+xml, are left out, so stored bytes cannot become stored XSS once they are served. Each allowed type is checked
// against the file's first bytes, so a declared type cannot lie about the content.
public static class MediaTypes
{
    // Enough of the file's start for every signature below.
    public const int SignatureLength = 16;

    public static IReadOnlyList<string> Allowed { get; } =
        ["image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf", "video/mp4", "video/webm"];

    public static bool IsAllowed(string? type)
    {
        return type is not null && Allowed.Contains(type, StringComparer.Ordinal);
    }

    // The start of the file, up to SignatureLength bytes, carries the signature of the type declared.
    public static bool Matches(string type, ReadOnlySpan<byte> head)
    {
        return type switch
        {
            "image/jpeg" => head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
            "image/png" => head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            "image/gif" => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
            "image/webp" => head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
            "application/pdf" => head.StartsWith("%PDF-"u8),
            "video/mp4" => head.Length >= 8 && head[4..8].SequenceEqual("ftyp"u8),
            "video/webm" => head.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]),
            _ => false,
        };
    }
}
