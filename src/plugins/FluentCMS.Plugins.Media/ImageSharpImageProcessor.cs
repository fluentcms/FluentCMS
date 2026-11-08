using FluentCMS.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace FluentCMS.Plugins.Media;

// The default image processor, with SixLabors.ImageSharp (Six Labors Split License). It reads and writes JPEG, PNG, GIF and
// WebP; it cannot encode AVIF, so f=auto never picks it.
internal sealed class ImageSharpImageProcessor : IImageProcessor
{
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "image/jpeg", "image/png", "image/gif", "image/webp" };

    public bool CanDecode(string mimeType) => Types.Contains(mimeType);

    public bool CanEncode(string mimeType) => Types.Contains(mimeType);

    public async Task<ImageHeader?> IdentifyAsync(Stream image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        try
        {
            var info = await Image.IdentifyAsync(image, cancellationToken);
            var turned = info.Metadata.ExifProfile is { } exif && exif.TryGetValue(ExifTag.Orientation, out var orientation)
                && orientation.Value is ExifOrientationMode.LeftTop or ExifOrientationMode.RightTop or ExifOrientationMode.RightBottom or ExifOrientationMode.LeftBottom;
            return turned
                ? new ImageHeader(info.Height, info.Width, Math.Max(1, info.FrameCount))
                : new ImageHeader(info.Width, info.Height, Math.Max(1, info.FrameCount));
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }
    }

    public async Task ProcessAsync(Stream image, ImageTransform transform, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(output);

        var encoder = EncoderOf(transform);
        Image decoded;
        try
        {
            decoded = await Image.LoadAsync(image, cancellationToken);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            throw new InvalidDataException("The bytes are no image ImageSharp reads.", exception);
        }

        using (decoded)
        {
            decoded.Mutate(context => context.AutoOrient());
            if (ResizeOf(transform, decoded.Width, decoded.Height) is { } resize)
            {
                decoded.Mutate(context => context.Resize(resize));
            }

            Strip(decoded);
            await decoded.SaveAsync(output, encoder, cancellationToken);
        }
    }

    // With one side, the other follows the aspect ratio; with both, the fit says how the image meets them.
    private static ResizeOptions? ResizeOf(ImageTransform transform, int width, int height)
    {
        var (w, h) = MediaTransforms.SizeOf(transform, width, height);
        switch (transform)
        {
            case { Width: not null, Height: not null }:
                return new ResizeOptions
                {
                    Size = new Size(w, h),
                    Mode = transform.Fit switch
                    {
                        ImageFit.Cover => ResizeMode.Crop,
                        ImageFit.Fill => ResizeMode.Stretch,
                        _ => ResizeMode.Max,
                    },
                    CenterCoordinates = transform.Fit == ImageFit.Cover && transform.FocalPoint is { } focal ? new PointF((float)focal.X, (float)focal.Y) : null,
                };
            case { Width: not null } or { Height: not null }:
                return new ResizeOptions { Size = new Size(w, h), Mode = ResizeMode.Stretch };
            default:
                return null;
        }
    }

    // What a camera records, such as where a photo was taken, has no place in an image served to anyone.
    private static void Strip(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        foreach (var frame in image.Frames)
        {
            frame.Metadata.ExifProfile = null;
            frame.Metadata.IptcProfile = null;
            frame.Metadata.XmpProfile = null;
        }
    }

    private static IImageEncoder EncoderOf(ImageTransform transform) => transform.MimeType switch
    {
        "image/jpeg" => new JpegEncoder { Quality = transform.Quality },
        "image/webp" => new WebpEncoder { Quality = transform.Quality, FileFormat = WebpFileFormatType.Lossy },
        "image/png" => new PngEncoder(),
        "image/gif" => new GifEncoder(),
        _ => throw new NotSupportedException($"ImageSharp does not encode {transform.MimeType} here."),
    };

    private static bool IsUnreadable(Exception exception) => exception is ImageFormatException or NotSupportedException or InvalidDataException;
}
