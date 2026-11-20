using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Media;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// An image to send: the bytes, which the caller disposes, their type, the entity tag of the variant, and whether the type
// came from the request's Accept.
internal sealed record TransformedImage(Stream Content, string MimeType, string ETag, bool VariesByAccept);

// GET /media/{id}/{fileName} in the site of the operation, for any caller: the URL is the authority, so no permission is
// checked, and only an asset that is public and not deleted is served, whatever the signature. In order: the signature,
// unless signed URLs are off; the parameters and the preset; the site's limits on width, height and operations; the type
// of the asset; then a variant already stored is sent. Otherwise the size the image declares is checked against the limit
// of source pixels before it is decoded, and the result is stored as a variant in the asset's storage and recorded.
internal sealed class MediaTransforms(
    PluginDbContext<MediaPlugin> database,
    IOperationContextAccessor operations,
    MediaLibraries libraries,
    ISettings<MediaSettings> settings,
    MediaStorage storages,
    IImageProcessor processor,
    MediaSigning signing,
    MediaDecodes decodes,
    IServiceScopeFactory scopes,
    IOptions<MediaOptions> options,
    TimeProvider time,
    ILogger<MediaTransforms> logger)
{
    public const int DefaultQuality = 80;

    // The thumbnail the Management API's reads give each image, for the media library and the picker: fitted inside 320 by
    // 320 pixels, as WebP where the processor encodes it. Its URL takes no size, so it serves that one and no other.
    public const string Thumbnail = "thumbnail";
    public const int ThumbnailSize = 320;

    private const string Avif = "image/avif";
    private const string Webp = "image/webp";
    private const string Png = "image/png";

    public static string ThumbnailCanonical(Guid id, long? expires, string? version) =>
        MediaOriginals.WithQuery($"{id:D}/{Thumbnail}", MediaOriginals.SignedQuery(expires, inline: false, version));

    public static async Task<string> ThumbnailUrlAsync(MediaSigning signing, Guid id, string fileName, long? expires, string? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signing);

        var signature = await signing.SignAsync(ThumbnailCanonical(id, expires, version), cancellationToken);
        return MediaOriginals.SignedUrl($"/media/{id:D}/{Thumbnail}/{Uri.EscapeDataString(fileName)}", MediaOriginals.SignedQuery(expires, inline: false, version), signature);
    }

    public async Task<TransformedImage> TransformAsync(Guid id, IEnumerable<KeyValuePair<string, string?>> query, IReadOnlyCollection<string> accepted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(accepted);

        var (parameters, signature) = ImageQuery.Split(query);
        if (options.Value.RequireSignedUrls && !await signing.VerifyAsync(ImageQuery.Canonical(id, parameters), signature, cancellationToken))
        {
            throw new PluginException(MediaErrorCodes.SignatureInvalid, signature is null ? "The URL of the image is not signed." : "The signature of the URL of the image is not valid.");
        }

        var requested = ImageQuery.Parse(parameters);
        var site = (operations.Current ?? throw new InvalidOperationException("An image is served inside an operation, which names the site.")).Site;
        var library = await libraries.OfAsync(site, cancellationToken);
        var asset = await database.Set<Asset>().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.SiteId == library.Value && item.DeletedAt == null && !item.IsPrivate, cancellationToken)
            ?? throw new PluginException(MediaErrorCodes.AssetNotFound, $"The site has no public media asset {id:D}.");

        var merged = requested;
        if (requested.Preset is { } name)
        {
            var preset = await database.Set<Preset>().AsNoTracking().FirstOrDefaultAsync(item => item.SiteId == library.Value && item.Name == name, cancellationToken)
                ?? throw new PluginException(MediaErrorCodes.PresetNotFound, $"The site has no media preset '{name}'.");
            merged = ImageQuery.Merge(PresetOptions(preset), requested);
        }

        return await RenderAsync(site, asset, merged, accepted, limited: true, cancellationToken);
    }

    // GET /media/{id}/thumbnail/{fileName}: the signature is always checked, whatever RequireSignedUrls says, since the size is
    // the plugin's and not held to the site's transform limits; MaxSourcePixels still guards the decoding. With exp, the URL
    // serves a private asset too, until it expires, as the Management API gives it to a caller who may read the asset. The
    // canonical string, {id}/thumbnail with exp and v, is neither a transform's {id}?… nor an original's {id}/original….
    public async Task<(TransformedImage Image, long? Expires)> ThumbnailAsync(Guid id, IEnumerable<KeyValuePair<string, string?>> query, CancellationToken cancellationToken)
    {
        var (signature, expires, _, version) = MediaOriginals.ReadQuery(query, "a thumbnail", takesDisposition: false);
        if (!await signing.VerifyAsync(ThumbnailCanonical(id, expires, version), signature, cancellationToken))
        {
            throw new PluginException(MediaErrorCodes.SignatureInvalid, signature is null ? "The URL of the thumbnail is not signed." : "The signature of the URL of the thumbnail is not valid.");
        }

        MediaOriginals.CheckExpiry(expires, time);

        var site = (operations.Current ?? throw new InvalidOperationException("An image is served inside an operation, which names the site.")).Site;
        var library = await libraries.OfAsync(site, cancellationToken);
        var expiring = expires is not null;
        var asset = await database.Set<Asset>().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.SiteId == library.Value && item.DeletedAt == null && (expiring || !item.IsPrivate), cancellationToken)
            ?? throw new PluginException(MediaErrorCodes.AssetNotFound, $"The site has no {(expiring ? "" : "public ")}media asset {id:D}.");

        var thumbnail = new MediaImageOptions
        {
            Width = ThumbnailSize,
            Height = ThumbnailSize,
            Fit = ImageFit.Contain,
            Format = processor.CanEncode(Webp) ? MediaImageFormat.Webp : null,
        };
        return (await RenderAsync(site, asset, thumbnail, [], limited: false, cancellationToken), expires);
    }

    // IMediaService.OpenImageAsync: a copy of an asset the caller may read, private or not, for code in the host. No URL
    // asks for it, so nothing is signed, and the type follows the options alone.
    public Task<TransformedImage> OpenAsync(SiteId site, Asset asset, MediaImageOptions options, CancellationToken cancellationToken) =>
        RenderAsync(site, asset, options, [], limited: true, cancellationToken);

    // Limited, the transform is held to the site's width, height and operations; the thumbnail, whose size is fixed, is not.
    private async Task<TransformedImage> RenderAsync(SiteId site, Asset asset, MediaImageOptions merged, IReadOnlyCollection<string> accepted, bool limited, CancellationToken cancellationToken)
    {
        var id = asset.Id;
        var rules = await settings.GetAsync(site, cancellationToken);
        if (limited)
        {
            CheckLimits(merged, rules);
        }
        if (!processor.CanDecode(asset.MimeType))
        {
            throw new PluginException(MediaErrorCodes.NotTransformable, $"The asset {id:D} is of the type {asset.MimeType}, which the image processor does not read.");
        }

        if ((long?)asset.Width * asset.Height > rules.MaxSourcePixels)
        {
            throw SourceTooLarge(id, rules);
        }

        var transform = Resolve(asset, merged, accepted);
        var hash = Hash(asset, transform);
        var etag = $"\"{hash}\"";
        var variesByAccept = merged.Format == MediaImageFormat.Auto;
        var assetStorage = StorageOf(asset);

        if (await StoredAsync(assetStorage, id, hash, cancellationToken) is { } stored)
        {
            return new TransformedImage(stored, transform.MimeType, etag, variesByAccept);
        }

        // The site and the limits are in the key, since the checks that the header allows are the caller's.
        await decodes.RunAsync($"{site}/{id:N}/{hash}/{limited}", token => CreateAsync(assetStorage, asset, transform, hash, rules, limited, token), cancellationToken);

        var made = await StoredAsync(assetStorage, id, hash, cancellationToken)
            ?? throw new PluginException(MediaErrorCodes.AssetNotFound, $"The variant {hash} of the media asset {id:D} is missing from the storage.");
        return new TransformedImage(made, transform.MimeType, etag, variesByAccept);
    }

    private async Task<Stream?> StoredAsync(IFileStorage assetStorage, Guid id, string hash, CancellationToken cancellationToken)
    {
        var stored = await database.Set<Variant>().AsNoTracking().FirstOrDefaultAsync(item => item.AssetId == id && item.ParamsHash == hash, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (await assetStorage.OpenReadAsync(stored.StorageKey, cancellationToken) is { } content)
        {
            return content;
        }

        await database.Set<Variant>().Where(item => item.AssetId == id && item.ParamsHash == hash).ExecuteDeleteAsync(cancellationToken);
        return null;
    }

    // Run by MediaDecodes for every caller that waits, so it outlives the scope of the request that started it and records
    // the variant in a scope of its own. The original is read once: from the local disk as it is, and from any other storage
    // copied to a temporary file first, where the header and then the pixels are read. The header is read even when the
    // asset recorded its size, since that size is before the orientation and counts no frames.
    private async Task CreateAsync(IFileStorage assetStorage, Asset asset, ImageTransform transform, string hash, MediaSettings rules, bool limited, CancellationToken cancellationToken)
    {
        var id = asset.Id;
        await using var scope = scopes.CreateAsyncScope();
        var variants = scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>();

        // A request that missed the variant just before an earlier run stored it starts a run after that one has ended.
        if (await variants.Set<Variant>().AsNoTracking().AnyAsync(item => item.AssetId == id && item.ParamsHash == hash, cancellationToken))
        {
            return;
        }

        var images = scope.ServiceProvider.GetRequiredService<IImageProcessor>();
        await using var source = await SpoolAsync(await OpenAsync(assetStorage, asset, cancellationToken), MediaService.TemporaryFile, cancellationToken);

        var header = await images.IdentifyAsync(source, cancellationToken)
            ?? throw new PluginException(MediaErrorCodes.NotTransformable, $"The bytes of the asset {id:D} are no image the image processor reads.");
        if ((long)header.Width * header.Height * header.FrameCount > rules.MaxSourcePixels)
        {
            throw SourceTooLarge(id, rules);
        }

        // A side that follows the aspect ratio is held to the same limits as a side the URL gives.
        var (width, height) = SizeOf(transform, header.Width, header.Height);
        if (limited && (width > rules.MaxTransformWidth || height > rules.MaxTransformHeight))
        {
            throw new PluginException(MediaErrorCodes.TransformLimit, $"The image would be {width} by {height} pixels, over the {rules.MaxTransformWidth} by {rules.MaxTransformHeight} the media settings of the site allow.");
        }

        await using var output = MediaService.TemporaryFile();
        source.Position = 0;
        try
        {
            await images.ProcessAsync(source, transform, output, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new PluginException(MediaErrorCodes.NotTransformable, $"The bytes of the asset {id:D} are no image the image processor reads: {exception.Message}");
        }

        var key = $"{asset.StorageKey[..asset.StorageKey.LastIndexOf('/')]}/{hash}{ExtensionOf(transform.MimeType)}";
        output.Position = 0;
        await assetStorage.WriteAsync(key, output, cancellationToken);
        try
        {
            await RecordAsync(variants, new Variant { AssetId = id, ParamsHash = hash, StorageKey = key, Size = output.Length, CreatedAt = time.GetUtcNow() }, cancellationToken);
        }
        catch
        {
            // Without its row, nothing would ever remove the file.
            await ForgetAsync(assetStorage, key);
            throw;
        }

        MediaLog.VariantCreated(logger, id, hash, transform.MimeType, output.Length);
    }

    // A stream on the local disk or in memory is read where it is; any other, such as a storage's that fetches at each seek,
    // is copied to a temporary file, deleted when it is disposed.
    internal static async Task<Stream> SpoolAsync(Stream source, Func<FileStream> temporaryFile, CancellationToken cancellationToken)
    {
        if (source is FileStream or MemoryStream)
        {
            return source;
        }

        await using (source)
        {
            var copy = temporaryFile();
            try
            {
                await source.CopyToAsync(copy, cancellationToken);
                copy.Position = 0;
                return copy;
            }
            catch
            {
                await copy.DisposeAsync();
                throw;
            }
        }
    }

    // The size the processor makes from an image of a size, shown after its orientation: with one side, the other follows
    // the aspect ratio; with both, a contain keeps the whole image inside them.
    public static (int Width, int Height) SizeOf(ImageTransform transform, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(transform);

        static int Scaled(long side, long by, long of) => (int)Math.Clamp((long)Math.Round((double)side * by / of), 1, int.MaxValue);

        return transform switch
        {
            { Width: { } w, Height: { } h, Fit: ImageFit.Contain } => (double)w / width <= (double)h / height ? (w, Scaled(height, w, width)) : (Scaled(width, h, height), h),
            { Width: { } w, Height: { } h } => (w, h),
            { Width: { } w } => (w, Scaled(height, w, width)),
            { Height: { } h } => (Scaled(width, h, height), h),
            _ => (width, height),
        };
    }

    public static MediaImageOptions PresetOptions(Preset preset)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(preset.Params) ?? [];
        return ImageQuery.Parse([.. values]);
    }

    public static string PresetParams(MediaImageOptions options) =>
        JsonSerializer.Serialize(ImageQuery.Of(options).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    public static void CheckLimits(MediaImageOptions options, MediaSettings rules)
    {
        var problem = options.Width > rules.MaxTransformWidth ? $"The width is at most {rules.MaxTransformWidth}."
            : options.Height > rules.MaxTransformHeight ? $"The height is at most {rules.MaxTransformHeight}."
            : ImageQuery.Operations(options) > rules.MaxTransformOperations ? $"A transform has at most {rules.MaxTransformOperations} operations."
            : null;
        if (problem is not null)
        {
            throw new PluginException(MediaErrorCodes.TransformLimit, $"{problem} The media settings of the site set the limits.");
        }
    }

    // f=auto takes AVIF, then WebP, where both the request accepts it and the processor encodes it, and the asset's own type
    // otherwise; an own type the processor does not encode, such as GIF with SkiaSharp, becomes PNG. A fit counts only with
    // both sides, and a focal point only for a cover, where the parameters win over the asset's own; what does not count is
    // left out, so that it makes no second variant of the same image.
    private ImageTransform Resolve(Asset asset, MediaImageOptions options, IReadOnlyCollection<string> accepted)
    {
        var own = processor.CanEncode(asset.MimeType) ? asset.MimeType : Png;
        var type = options.Format switch
        {
            MediaImageFormat.Auto => accepted.Contains(Avif) && processor.CanEncode(Avif) ? Avif
                : accepted.Contains(Webp) && processor.CanEncode(Webp) ? Webp
                : own,
            MediaImageFormat.Webp => Webp,
            MediaImageFormat.Avif => Avif,
            MediaImageFormat.Jpeg => "image/jpeg",
            MediaImageFormat.Png => Png,
            _ => own,
        };

        if (!processor.CanEncode(type))
        {
            throw MediaService.Invalid(ImageQuery.Format, $"The image processor does not encode {type}.");
        }

        var fit = options is { Width: not null, Height: not null } ? options.Fit ?? ImageFit.Contain : ImageFit.Contain;
        var focal = fit == ImageFit.Cover ? options.FocalPoint ?? (asset is { FocalX: { } x, FocalY: { } y } ? new MediaFocalPoint(x, y) : null) : null;
        return new ImageTransform
        {
            Width = options.Width,
            Height = options.Height,
            Fit = fit,
            Quality = options.Quality ?? DefaultQuality,
            MimeType = type,
            FocalPoint = focal,
        };
    }

    private static string Hash(Asset asset, ImageTransform transform)
    {
        var resolved = string.Join(
            ';',
            asset.Checksum,
            transform.Width?.ToString(CultureInfo.InvariantCulture),
            transform.Height?.ToString(CultureInfo.InvariantCulture),
            transform.Fit,
            transform.Quality.ToString(CultureInfo.InvariantCulture),
            transform.MimeType,
            transform.FocalPoint?.X.ToString("R", CultureInfo.InvariantCulture),
            transform.FocalPoint?.Y.ToString("R", CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(resolved)));
    }

    // Two requests that make the same variant at once write the same key and meet at the primary key.
    private static async Task RecordAsync(PluginDbContext<MediaPlugin> context, Variant variant, CancellationToken cancellationToken)
    {
        context.Add(variant);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.Entry(variant).State = EntityState.Detached;
            if (!await context.Set<Variant>().AsNoTracking().AnyAsync(item => item.AssetId == variant.AssetId && item.ParamsHash == variant.ParamsHash, CancellationToken.None))
            {
                throw;
            }

            return;
        }

        context.Entry(variant).State = EntityState.Detached;
    }

    private IFileStorage StorageOf(Asset asset) => storages.Of(asset.StorageProvider);

    private async Task ForgetAsync(IFileStorage target, string key)
    {
        try
        {
            await target.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            MediaLog.NotDeleted(logger, target.Name, key, exception);
        }
    }

    private static async Task<Stream> OpenAsync(IFileStorage storage, Asset asset, CancellationToken cancellationToken) =>
        await storage.OpenReadAsync(asset.StorageKey, cancellationToken)
        ?? throw new PluginException(MediaErrorCodes.AssetNotFound, $"The bytes of the media asset {asset.Id:D} are missing from the storage.");

    private static PluginException SourceTooLarge(Guid id, MediaSettings rules) =>
        new(MediaErrorCodes.TransformLimit, $"The asset {id:D} has more than the {rules.MaxSourcePixels} pixels the site transforms.");

    private static string ExtensionOf(string mimeType) => MediaTypes.Allowable.FirstOrDefault(type => type.MimeType == mimeType)?.Extension ?? ".bin";
}
