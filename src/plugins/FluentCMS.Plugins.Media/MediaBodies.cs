using System.ComponentModel;
using FluentCMS.Content;
using FluentCMS.Media;

namespace FluentCMS.Plugins.Media;

// An asset as the Management API reads it: its metadata and the URLs of the media library and the picker.
internal sealed record ManagedAsset(MediaAsset Asset, MediaAssetUrls Urls);

internal sealed record MediaAssetUrls(
    [property: Description("The original file, /media/{id}/original/{fileName}, signed. A private asset's URL serves it inline until expiresAt.")]
    string Original,
    [property: Description("For an image the image processor reads, /media/{id}/thumbnail/{fileName}, signed: the image fitted inside 320 by 320 pixels, as WebP where the processor encodes it. Null for any other file.")]
    string? Thumbnail,
    [property: Description("For a private asset, when both URLs stop working: at most the site's media setting downloadUrlSeconds after the read, and more than half of it. Null for a public asset, whose URLs do not expire.")]
    DateTimeOffset? ExpiresAt);

// The bodies of GET /media/{id} and GET /media: the members of MediaAsset as they were written before, and urls.
internal sealed record MediaAssetResponse(
    Guid Id,
    Guid SiteId,
    Guid? FolderId,
    string FileName,
    string MimeType,
    long Size,
    string Checksum,
    int? Width,
    int? Height,
    string? Title,
    string Locale,
    string? Alt,
    string? Caption,
    string? Credit,
    IReadOnlyList<string> Tags,
    MediaFocalPoint? FocalPoint,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy,
    MediaAssetUrls Urls)
{
    public static MediaAssetResponse From(ManagedAsset managed)
    {
        ArgumentNullException.ThrowIfNull(managed);

        var asset = managed.Asset;
        return new(
            asset.Id,
            asset.SiteId,
            asset.FolderId,
            asset.FileName,
            asset.MimeType,
            asset.Size,
            asset.Checksum,
            asset.Width,
            asset.Height,
            asset.Title,
            asset.Locale,
            asset.Alt,
            asset.Caption,
            asset.Credit,
            asset.Tags,
            asset.FocalPoint,
            asset.IsPrivate,
            asset.CreatedAt,
            asset.CreatedBy,
            asset.UpdatedAt,
            asset.UpdatedBy,
            managed.Urls);
    }
}

internal sealed record MediaAssetPage(IReadOnlyList<MediaAssetResponse> Items, long Total, int Page, int PageSize)
{
    public static MediaAssetPage From(PagedResult<ManagedAsset> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new([.. page.Items.Select(MediaAssetResponse.From)], page.Total ?? 0, page.Page ?? 1, page.PageSize);
    }
}
