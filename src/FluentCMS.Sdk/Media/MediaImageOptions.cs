namespace FluentCMS.Media;

/// <summary>
/// The parameters of an image transform URL, <c>/media/{id}/{fileName}?w=&amp;h=&amp;fit=&amp;q=&amp;f=&amp;fx=&amp;fy=&amp;preset=</c>,
/// and the parameters a preset stores. A member left null is not part of the URL, and a parameter of the URL wins over the
/// same one of its preset.
/// </summary>
public sealed record MediaImageOptions
{
    /// <summary>The width in pixels (<c>w</c>).</summary>
    public int? Width { get; init; }

    /// <summary>The height in pixels (<c>h</c>).</summary>
    public int? Height { get; init; }

    /// <summary>How the image fits a width and a height given together (<c>fit</c>); <see cref="ImageFit.Contain"/> by default.</summary>
    public ImageFit? Fit { get; init; }

    /// <summary>The quality of a lossy encoding, from 1 to 100 (<c>q</c>); 80 by default.</summary>
    public int? Quality { get; init; }

    /// <summary>The type to encode (<c>f</c>); the type of the asset by default.</summary>
    public MediaImageFormat? Format { get; init; }

    /// <summary>The point that <see cref="ImageFit.Cover"/> keeps in the crop (<c>fx</c> and <c>fy</c>); the asset's own by default.</summary>
    public MediaFocalPoint? FocalPoint { get; init; }

    /// <summary>The name of a preset of the site whose parameters apply under these (<c>preset</c>). A preset names none.</summary>
    public string? Preset { get; init; }
}

/// <summary>The type an image transform encodes.</summary>
public enum MediaImageFormat
{
    /// <summary>WebP, or AVIF where the processor can encode it, when the request's <c>Accept</c> lists it; the type of the asset otherwise.</summary>
    Auto,

    /// <summary>WebP.</summary>
    Webp,

    /// <summary>AVIF, where the processor can encode it.</summary>
    Avif,

    /// <summary>JPEG.</summary>
    Jpeg,

    /// <summary>PNG.</summary>
    Png,
}

/// <summary>A named set of transform parameters of a site, which a URL applies with <c>preset=</c>.</summary>
/// <param name="Name">The name, of lower-case letters, digits and hyphens.</param>
/// <param name="Options">The parameters, without a preset.</param>
public sealed record MediaPreset(string Name, MediaImageOptions Options);
