using System.Globalization;
using FluentCMS.Content;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// An original file to send: the bytes, which the caller disposes, their type and checksum, the file name, and whether a
// download URL asked for it.
internal sealed record OriginalFile(Stream Content, string MimeType, string Checksum, string FileName, bool Download);

// GET /media/{id}/original/{fileName} in the site of the operation, for any caller: as for transforms, the URL is the
// authority and the file name is ignored. A URL with exp is a download URL, whose signature is checked whatever
// RequireSignedUrls says; it serves a private asset too, and is refused from its expiry on. A URL without it serves a public
// asset only, signed unless signed URLs are off. A soft-deleted asset is never served. The canonical string of a signature
// starts with the id and "/original", so a transform's signature, which follows the id with "?", never fits it.
internal sealed class MediaOriginals(
    PluginDbContext<MediaPlugin> database,
    IOperationContextAccessor operations,
    MediaLibraries libraries,
    MediaStorage storages,
    MediaSigning signing,
    IOptions<MediaOptions> options,
    TimeProvider time)
{
    public const string Expires = "exp";

    public static string Canonical(Guid id, long? expires) =>
        expires is { } at ? $"{id:D}/original?{Expires}={at.ToString(CultureInfo.InvariantCulture)}" : $"{id:D}/original";

    public static async Task<string> UrlAsync(MediaSigning signing, Guid id, string fileName, long? expires, CancellationToken cancellationToken)
    {
        var signature = await signing.SignAsync(Canonical(id, expires), cancellationToken);
        var query = expires is { } at ? $"{Expires}={at.ToString(CultureInfo.InvariantCulture)}&" : "";
        return $"/media/{id:D}/original/{Uri.EscapeDataString(fileName)}?{query}{ImageQuery.Signature}={signature}";
    }

    public async Task<OriginalFile> OpenAsync(Guid id, IEnumerable<KeyValuePair<string, string?>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        string? signature = null;
        long? expires = null;
        var signed = false;
        foreach (var (name, value) in query)
        {
            switch (name)
            {
                case ImageQuery.Signature when !signed:
                    signature = value;
                    signed = true;
                    break;
                case Expires when expires is null:
                    expires = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var at)
                        ? at
                        : throw MediaService.Invalid(Expires, "The expiry of a download URL is a number of seconds since 1970.");
                    break;
                case ImageQuery.Site:
                    break;
                default:
                    throw MediaService.Invalid(name, $"The URL of an original file takes {Expires} and {ImageQuery.Signature}, each at most once, and {ImageQuery.Site}.");
            }
        }

        var download = expires is not null;
        if ((download || options.Value.RequireSignedUrls) && !await signing.VerifyAsync(Canonical(id, expires), signature, cancellationToken))
        {
            throw new PluginException(MediaErrorCodes.SignatureInvalid, signature is null ? "The URL of the file is not signed." : "The signature of the URL of the file is not valid.");
        }

        if (expires is { } expiry && time.GetUtcNow().ToUnixTimeSeconds() >= expiry)
        {
            throw new PluginException(MediaErrorCodes.DownloadExpired, "The download URL has expired; ask for a new one.");
        }

        var site = (operations.Current ?? throw new InvalidOperationException("A file is served inside an operation, which names the site.")).Site;
        var library = await libraries.OfAsync(site, cancellationToken);
        var asset = await database.Set<Asset>().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.SiteId == library.Value && item.DeletedAt == null && (download || !item.IsPrivate), cancellationToken)
            ?? throw new PluginException(MediaErrorCodes.AssetNotFound, $"The site has no {(download ? "" : "public ")}media asset {id:D}.");

        var content = await storages.Of(asset.StorageProvider).OpenReadAsync(asset.StorageKey, cancellationToken)
            ?? throw new PluginException(MediaErrorCodes.AssetNotFound, $"The bytes of the media asset {asset.Id:D} are missing from the storage.");
        return new OriginalFile(content, asset.MimeType, asset.Checksum, asset.FileName, download);
    }
}
