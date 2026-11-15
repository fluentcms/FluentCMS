using System.Globalization;
using FluentCMS.Content;
using FluentCMS.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Media;

// An original file to send: the bytes, which the caller disposes, the type to send them as, their checksum, the file name,
// the expiry of the URL that asked for it, whether that URL is a download URL, and whether they are sent as an attachment.
internal sealed record OriginalFile(Stream Content, string MimeType, string Checksum, string FileName, long? Expires, bool Download, bool Attachment);

// GET /media/{id}/original/{fileName} in the site of the operation, for any caller: as for transforms, the URL is the
// authority and the file name is ignored. A URL with exp is a short-lived URL, whose signature is checked whatever
// RequireSignedUrls says; it serves a private asset too, and is refused from its expiry on. It is a download URL, sent as an
// attachment, unless it carries disposition=inline, which the signature covers, as the Management API's reads give for a
// private asset. A URL without exp serves a public asset only, signed unless signed URLs are off. A soft-deleted asset is
// never served. The canonical string of a signature starts with the id and "/original", so a transform's signature, which
// follows the id with "?", never fits it. A file is sent inline only as a type the plugin finds from bytes, named as the
// plugin names it; one stored with any other type, such as an import took from a package before it checked the files, is
// sent as application/octet-stream, as an attachment.
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
    public const string Disposition = "disposition";
    public const string Inline = "inline";

    public const string OctetStream = "application/octet-stream";

    public static string Canonical(Guid id, long? expires, bool inline = false) =>
        expires is { } at ? $"{id:D}/original?{ExpiryQuery(at, inline)}" : $"{id:D}/original";

    public static async Task<string> UrlAsync(MediaSigning signing, Guid id, string fileName, long? expires, CancellationToken cancellationToken, bool inline = false)
    {
        var signature = await signing.SignAsync(Canonical(id, expires, inline), cancellationToken);
        var query = expires is { } at ? $"{ExpiryQuery(at, inline)}&" : "";
        return $"/media/{id:D}/original/{Uri.EscapeDataString(fileName)}?{query}{ImageQuery.Signature}={signature}";
    }

    // The parameters of a URL of a file sent as it is stored or as its thumbnail: sig and exp, each at most once, and site,
    // which the host reads; disposition=inline only where it is taken, and only with exp.
    public static (string? Signature, long? Expires, bool Inline) ReadQuery(IEnumerable<KeyValuePair<string, string?>> query, string what, bool takesDisposition)
    {
        ArgumentNullException.ThrowIfNull(query);

        string? signature = null;
        long? expires = null;
        bool? inline = null;
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
                        : throw MediaService.Invalid(Expires, "The expiry of a URL is a number of seconds since 1970.");
                    break;
                case Disposition when takesDisposition && inline is null:
                    inline = value == Inline ? true : throw MediaService.Invalid(Disposition, $"The one {Disposition} a URL names is {Inline}.");
                    break;
                case ImageQuery.Site:
                    break;
                default:
                    throw MediaService.Invalid(name, takesDisposition
                        ? $"The URL of {what} takes {Expires}, {Disposition} and {ImageQuery.Signature}, each at most once, and {ImageQuery.Site}."
                        : $"The URL of {what} takes {Expires} and {ImageQuery.Signature}, each at most once, and {ImageQuery.Site}.");
            }
        }

        if (inline is true && expires is null)
        {
            throw MediaService.Invalid(Disposition, $"Only a URL with {Expires} names a {Disposition}; the file of a public asset is sent inline.");
        }

        return (signature, expires, inline is true);
    }

    public static void CheckExpiry(long? expires, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        if (expires is { } expiry && time.GetUtcNow().ToUnixTimeSeconds() >= expiry)
        {
            throw new PluginException(MediaErrorCodes.DownloadExpired, "The URL has expired; ask for a new one.");
        }
    }

    // A URL with an expiry may serve a private file, so no shared cache keeps the answer, and a browser keeps it no longer
    // than the URL works.
    public static string PrivateCacheControl(long expires, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        return $"private, max-age={Math.Max(0, expires - time.GetUtcNow().ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture)}";
    }

    public async Task<OriginalFile> OpenAsync(Guid id, IEnumerable<KeyValuePair<string, string?>> query, CancellationToken cancellationToken)
    {
        var (signature, expires, inline) = ReadQuery(query, "an original file", takesDisposition: true);
        if ((expires is not null || options.Value.RequireSignedUrls) && !await signing.VerifyAsync(Canonical(id, expires, inline), signature, cancellationToken))
        {
            throw new PluginException(MediaErrorCodes.SignatureInvalid, signature is null ? "The URL of the file is not signed." : "The signature of the URL of the file is not valid.");
        }

        CheckExpiry(expires, time);

        var site = (operations.Current ?? throw new InvalidOperationException("A file is served inside an operation, which names the site.")).Site;
        var library = await libraries.OfAsync(site, cancellationToken);
        var expiring = expires is not null;
        var asset = await database.Set<Asset>().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.SiteId == library.Value && item.DeletedAt == null && (expiring || !item.IsPrivate), cancellationToken)
            ?? throw new PluginException(MediaErrorCodes.AssetNotFound, $"The site has no {(expiring ? "" : "public ")}media asset {id:D}.");

        var content = await storages.Of(asset.StorageProvider).OpenReadAsync(asset.StorageKey, cancellationToken)
            ?? throw new PluginException(MediaErrorCodes.AssetNotFound, $"The bytes of the media asset {asset.Id:D} are missing from the storage.");
        var type = MediaTypes.Find(asset.MimeType);
        var download = expiring && !inline;
        return new OriginalFile(content, type?.MimeType ?? OctetStream, asset.Checksum, asset.FileName, expires, download, download || type is null);
    }

    // Sorted by name, as a canonical string is, so that the query the URL carries is the one the signature covers.
    private static string ExpiryQuery(long expires, bool inline) =>
        $"{(inline ? $"{Disposition}={Inline}&" : "")}{Expires}={expires.ToString(CultureInfo.InvariantCulture)}";
}
