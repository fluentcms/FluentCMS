using FluentCMS.Media;
using SkiaSharp;

namespace FluentCMS.Plugins.Media;

// The default image processor, with SkiaSharp (MIT). It reads JPEG, PNG, GIF (its first frame) and WebP, and writes JPEG,
// PNG and WebP. Skia encodes neither AVIF nor GIF, so f=auto never picks AVIF, and a GIF keeps no type of its own. Skia's
// encoders write no EXIF, IPTC or XMP, so the metadata of the original stays behind.
internal sealed class SkiaImageProcessor : IImageProcessor
{
    private static readonly HashSet<string> Decodable = new(StringComparer.Ordinal) { "image/jpeg", "image/png", "image/gif", "image/webp" };

    private static readonly Dictionary<string, SKEncodedImageFormat> Encodable = new(StringComparer.Ordinal)
    {
        ["image/jpeg"] = SKEncodedImageFormat.Jpeg,
        ["image/png"] = SKEncodedImageFormat.Png,
        ["image/webp"] = SKEncodedImageFormat.Webp,
    };

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    // A photo in a wider gamut, such as Display P3, is converted when it is decoded, since the encoders write sRGB.
    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    public bool CanDecode(string mimeType) => Decodable.Contains(mimeType);

    public bool CanEncode(string mimeType) => Encodable.ContainsKey(mimeType);

    // SKCodec reads the header only; the pixels are decoded by ProcessAsync.
    public async Task<ImageHeader?> IdentifyAsync(Stream image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        await using var buffered = await BufferedAsync(image, cancellationToken);
        using var codec = SKCodec.Create(buffered ?? image);
        if (codec is null)
        {
            return null;
        }

        var (width, height) = IsTurned(codec.EncodedOrigin) ? (codec.Info.Height, codec.Info.Width) : (codec.Info.Width, codec.Info.Height);
        return new ImageHeader(width, height, Math.Max(1, codec.FrameCount));
    }

    public async Task ProcessAsync(Stream image, ImageTransform transform, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(output);

        var format = Encodable.TryGetValue(transform.MimeType, out var known) ? known : throw new NotSupportedException($"SkiaSharp does not encode {transform.MimeType} here.");
        await using var buffered = await BufferedAsync(image, cancellationToken);
        using var encoded = Encode(buffered ?? image, transform, format);
        await using var bytes = encoded.AsStream();
        await bytes.CopyToAsync(output, cancellationToken);
    }

    private static SKData Encode(Stream image, ImageTransform transform, SKEncodedImageFormat format)
    {
        using var codec = SKCodec.Create(image) ?? throw new InvalidDataException("The bytes are no image SkiaSharp reads.");
        using var decoded = Decode(codec);
        using var result = Render(decoded, codec.EncodedOrigin, transform);
        return result.Encode(format, transform.Quality) ?? throw new InvalidOperationException($"SkiaSharp could not encode the image as {transform.MimeType}.");
    }

    // An image that is not whole, such as one cut short, is refused rather than served with its missing part blank.
    private static SKImage Decode(SKCodec codec)
    {
        var info = codec.Info.WithColorType(SKImageInfo.PlatformColorType).WithAlphaType(codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul).WithColorSpace(Srgb);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException($"SkiaSharp could not decode the image: {result}.");
        }

        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap) ?? throw new InvalidDataException("SkiaSharp could not decode the image.");
    }

    // The EXIF orientation as a matrix from the stored pixels to the image as shown.
    private static SKMatrix OrientationOf(SKEncodedOrigin origin, float w, float h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix { ScaleX = -1, TransX = w, ScaleY = 1, Persp2 = 1 },
        SKEncodedOrigin.BottomRight => new SKMatrix { ScaleX = -1, TransX = w, ScaleY = -1, TransY = h, Persp2 = 1 },
        SKEncodedOrigin.BottomLeft => new SKMatrix { ScaleX = 1, ScaleY = -1, TransY = h, Persp2 = 1 },
        SKEncodedOrigin.LeftTop => new SKMatrix { SkewX = 1, SkewY = 1, Persp2 = 1 },
        SKEncodedOrigin.RightTop => new SKMatrix { SkewX = -1, TransX = h, SkewY = 1, Persp2 = 1 },
        SKEncodedOrigin.RightBottom => new SKMatrix { SkewX = -1, TransX = h, SkewY = -1, TransY = w, Persp2 = 1 },
        SKEncodedOrigin.LeftBottom => new SKMatrix { SkewX = 1, SkewY = -1, TransY = w, Persp2 = 1 },
        _ => SKMatrix.Identity,
    };

    // One draw turns the stored pixels as the orientation says and maps the part of the image shown, all of it or for a
    // cover the part around the focal point with the aspect ratio asked for, onto the size that MediaTransforms.SizeOf
    // gives, which the limits were checked with.
    private static SKImage Render(SKImage image, SKEncodedOrigin origin, ImageTransform transform)
    {
        var (shownWidth, shownHeight) = IsTurned(origin) ? (image.Height, image.Width) : (image.Width, image.Height);
        var (width, height) = MediaTransforms.SizeOf(transform, shownWidth, shownHeight);
        var source = transform is { Width: not null, Height: not null, Fit: ImageFit.Cover }
            ? CropOf(shownWidth, shownHeight, width, height, transform.FocalPoint ?? new MediaFocalPoint(0.5, 0.5))
            : SKRect.Create(shownWidth, shownHeight);

        using var surface = Surface(width, height, image.AlphaType);
        var canvas = surface.Canvas;
        canvas.Scale(width / source.Width, height / source.Height);
        canvas.Translate(-source.Left, -source.Top);
        canvas.Concat(OrientationOf(origin, image.Width, image.Height));
        canvas.DrawImage(image, 0, 0, Sampling);
        return surface.Snapshot();
    }

    private static SKRect CropOf(int imageWidth, int imageHeight, int width, int height, MediaFocalPoint focal)
    {
        var scale = Math.Max((double)width / imageWidth, (double)height / imageHeight);
        var cropWidth = Math.Min(imageWidth, width / scale);
        var cropHeight = Math.Min(imageHeight, height / scale);
        var left = Math.Clamp((focal.X * imageWidth) - (cropWidth / 2), 0, imageWidth - cropWidth);
        var top = Math.Clamp((focal.Y * imageHeight) - (cropHeight / 2), 0, imageHeight - cropHeight);
        return SKRect.Create((float)left, (float)top, (float)cropWidth, (float)cropHeight);
    }

    private static SKSurface Surface(int width, int height, SKAlphaType alpha) =>
        SKSurface.Create(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, alpha == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul, Srgb))
        ?? throw new InvalidOperationException($"SkiaSharp could not make a surface of {width} by {height} pixels.");

    private static bool IsTurned(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    // Skia's codecs rewind, which a stream that cannot seek allows only within a small buffer, so such a stream is read
    // into memory first; null for one that can seek. An original is at most the site's file size limit.
    private static async Task<MemoryStream?> BufferedAsync(Stream image, CancellationToken cancellationToken)
    {
        if (image.CanSeek)
        {
            return null;
        }

        var copy = new MemoryStream();
        await image.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }
}
