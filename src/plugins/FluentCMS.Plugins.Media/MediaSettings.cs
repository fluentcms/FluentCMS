using System.ComponentModel.DataAnnotations;
using FluentCMS.Settings;

namespace FluentCMS.Plugins.Media;

// The rules of uploads, the limits of image transforms and the lifetime of download URLs in a site, in the settings area
// "media" (settings.media.manage). An SVG is governed by Svg alone, since it can carry script; the allow-list holds the
// other types the plugin reads from bytes. A transform over a limit is refused before the image is read; MaxSourcePixels
// counts every frame of an animation.
[Settings("media")]
public sealed class MediaSettings : IValidatableObject
{
    [Range(1, long.MaxValue)]
    public long MaxFileSize { get; set; } = 50L * 1024 * 1024;

    [Range(1, long.MaxValue)]
    public long MaxRequestSize { get; set; } = 200L * 1024 * 1024;

    [Range(1, 100)]
    public int MaxFilesPerRequest { get; set; } = 20;

    public List<string> AllowedTypes { get; set; } = [.. MediaTypes.Allowable.Select(type => type.MimeType)];

    public SvgHandling Svg { get; set; } = SvgHandling.Refuse;

    [Range(1, 16384)]
    public int MaxTransformWidth { get; set; } = 4096;

    [Range(1, 16384)]
    public int MaxTransformHeight { get; set; } = 4096;

    [Range(1, 6)]
    public int MaxTransformOperations { get; set; } = 6;

    [Range(1, long.MaxValue)]
    public long MaxSourcePixels { get; set; } = 50_000_000;

    [Range(10, 86400)]
    public int DownloadUrlSeconds { get; set; } = 300;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var type in AllowedTypes ?? [])
        {
            if (type == MediaTypes.Svg.MimeType)
            {
                yield return new ValidationResult($"An SVG is allowed by {nameof(Svg)}, not by the list.", [nameof(AllowedTypes)]);
            }
            else if (MediaTypes.Allowable.All(known => known.MimeType != type))
            {
                yield return new ValidationResult($"'{type}' is not a type the plugin reads from bytes: {string.Join(", ", MediaTypes.Allowable.Select(known => known.MimeType))}.", [nameof(AllowedTypes)]);
            }
        }
    }
}

public enum SvgHandling
{
    // Every SVG is refused.
    Refuse,

    // Scripts, event handlers, foreignObject and javascript: URLs are taken out, and the rest is stored.
    Sanitize,
}
