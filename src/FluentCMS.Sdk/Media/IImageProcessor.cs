namespace FluentCMS.Media;

/// <summary>
/// Decodes, resizes and encodes the images of the media plugin's transforms: a provider slot whose default uses
/// SkiaSharp, replaced by a plugin or the host. The plugin checks the limits of the site before it calls the
/// processor, and the size an image declares, read with <see cref="IdentifyAsync"/>, before it decodes the image.
/// </summary>
public interface IImageProcessor
{
    /// <summary>Whether the processor can read images of a type.</summary>
    /// <param name="mimeType">The MIME type, such as <c>image/jpeg</c>.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    bool CanDecode(string mimeType);

    /// <summary>Whether the processor can write images of a type.</summary>
    /// <param name="mimeType">The MIME type, such as <c>image/webp</c>.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    bool CanEncode(string mimeType);

    /// <summary>
    /// Reads the size of an image from its header, without decoding its pixels: the width and height as the image is shown,
    /// after its orientation, which <see cref="ProcessAsync"/> applies.
    /// </summary>
    /// <param name="image">The bytes of the image, which the caller disposes.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The size, or <see langword="null"/> when the bytes are no image the processor reads.</returns>
    Task<ImageHeader?> IdentifyAsync(Stream image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decodes an image, applies its orientation, resizes it, drops its EXIF, IPTC and XMP metadata, and encodes it.
    /// </summary>
    /// <param name="image">The bytes of the image, which the caller disposes.</param>
    /// <param name="transform">What to do.</param>
    /// <param name="output">Where the encoded image is written.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the image is written.</returns>
    /// <exception cref="InvalidDataException">The bytes are no image the processor reads.</exception>
    Task ProcessAsync(Stream image, ImageTransform transform, Stream output, CancellationToken cancellationToken = default);
}

/// <summary>What the header of an image declares.</summary>
/// <param name="Width">The width in pixels, after the orientation of the image.</param>
/// <param name="Height">The height in pixels, after the orientation of the image.</param>
/// <param name="FrameCount">The number of frames, more than one for an animation.</param>
public sealed record ImageHeader(int Width, int Height, int FrameCount);

/// <summary>
/// One transform of an image, resolved: the size to fit, how to fit it, and the type and quality to encode. With only a
/// width or only a height, the other follows the aspect ratio of the image; with neither, the image keeps its size.
/// </summary>
public sealed record ImageTransform
{
    /// <summary>The width in pixels, or <see langword="null"/> to follow the height.</summary>
    public int? Width { get; init; }

    /// <summary>The height in pixels, or <see langword="null"/> to follow the width.</summary>
    public int? Height { get; init; }

    /// <summary>How the image fits a width and a height given together.</summary>
    public ImageFit Fit { get; init; } = ImageFit.Contain;

    /// <summary>The quality of a lossy encoding, from 1 to 100.</summary>
    public int Quality { get; init; } = 80;

    /// <summary>The MIME type to encode, one the processor can encode.</summary>
    public required string MimeType { get; init; }

    /// <summary>The point that <see cref="ImageFit.Cover"/> keeps in the crop, or <see langword="null"/> for the centre.</summary>
    public MediaFocalPoint? FocalPoint { get; init; }
}

/// <summary>How an image fits a width and a height given together.</summary>
public enum ImageFit
{
    /// <summary>The whole image, scaled to fit inside the box with its aspect ratio, so one side may be shorter.</summary>
    Contain,

    /// <summary>The box filled with the aspect ratio kept, the rest cropped around the focal point.</summary>
    Cover,

    /// <summary>The box filled exactly, the image stretched to it.</summary>
    Fill,
}
