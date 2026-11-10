using System.Text.Json.Serialization;

namespace FluentCMS.Media;

/// <summary>An asset of the media plugin, with its alternative text and caption in one locale.</summary>
public sealed record MediaAsset
{
    /// <summary>The id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The site it belongs to.</summary>
    public required Guid SiteId { get; init; }

    /// <summary>Its folder, or <see langword="null"/> for the root.</summary>
    public Guid? FolderId { get; init; }

    /// <summary>The name of the file as it was uploaded, without a directory.</summary>
    public required string FileName { get; init; }

    /// <summary>The MIME type read from its bytes.</summary>
    public required string MimeType { get; init; }

    /// <summary>Its length in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>The SHA-256 of its bytes, in lower-case hex.</summary>
    public required string Checksum { get; init; }

    /// <summary>The width of an image in pixels, when it could be read.</summary>
    public int? Width { get; init; }

    /// <summary>The height of an image in pixels, when it could be read.</summary>
    public int? Height { get; init; }

    /// <summary>The title.</summary>
    public string? Title { get; init; }

    /// <summary>The locale of <see cref="Alt"/> and <see cref="Caption"/>.</summary>
    public required string Locale { get; init; }

    /// <summary>The alternative text in <see cref="Locale"/>.</summary>
    public string? Alt { get; init; }

    /// <summary>The caption in <see cref="Locale"/>.</summary>
    public string? Caption { get; init; }

    /// <summary>Who to credit.</summary>
    public string? Credit { get; init; }

    /// <summary>The tags.</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>The point of an image to keep in view when it is cropped, or <see langword="null"/> for its centre.</summary>
    public MediaFocalPoint? FocalPoint { get; init; }

    /// <summary>Whether the asset is private, so that only a signed URL serves it.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>When it was uploaded.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Who uploaded it.</summary>
    public required Guid CreatedBy { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Who last changed it.</summary>
    public required Guid UpdatedBy { get; init; }
}

/// <summary>A point of an image, as fractions of its width and height from the top left corner, each from 0 to 1.</summary>
/// <param name="X">The fraction of the width.</param>
/// <param name="Y">The fraction of the height.</param>
[JsonNumberHandling(JsonNumberHandling.Strict)]
public readonly record struct MediaFocalPoint(double X, double Y);

/// <summary>A folder of the media plugin.</summary>
/// <param name="Id">The id.</param>
/// <param name="SiteId">The site it belongs to.</param>
/// <param name="ParentId">Its parent, or <see langword="null"/> for the root.</param>
/// <param name="Name">Its name, unique within its parent.</param>
public sealed record MediaFolder(Guid Id, Guid SiteId, Guid? ParentId, string Name);

/// <summary>A signed URL of the original file of an asset, and when it stops working.</summary>
/// <param name="Url">The URL, relative to the base of the CMS.</param>
/// <param name="ExpiresAt">When the URL is refused from.</param>
public sealed record MediaDownloadUrl(string Url, DateTimeOffset ExpiresAt);

/// <summary>An asset as the Delivery API gives it.</summary>
/// <param name="Asset">The asset, with its alternative text and caption in the locale of the operation.</param>
/// <param name="Urls">Its URLs, or <see langword="null"/> for a private asset, whose file only a download URL serves.</param>
public sealed record MediaDelivery(MediaAsset Asset, MediaUrls? Urls);

/// <summary>The URLs of a public asset, relative to the base of the CMS, each signed when the instance requires signed URLs.</summary>
/// <param name="Original">The original file, <c>/media/{id}/original/{fileName}</c>.</param>
/// <param name="Presets">For an image the image processor reads, a transform URL for each preset of the site, by name; otherwise empty.</param>
/// <param name="Template">
/// For such an image where the instance serves unsigned URLs, a transform URL whose <c>{w}</c>, <c>{h}</c>, <c>{fit}</c>,
/// <c>{q}</c> and <c>{f}</c> a client fills in or leaves out with their parameter; otherwise <see langword="null"/>.
/// </param>
public sealed record MediaUrls(string Original, IReadOnlyDictionary<string, string> Presets, string? Template);

/// <summary>Files to store as new assets.</summary>
/// <param name="FolderId">The folder they go to, or <see langword="null"/> for the root.</param>
/// <param name="Files">The files.</param>
public sealed record MediaUpload(Guid? FolderId, IReadOnlyList<MediaUploadFile> Files);

/// <summary>One file to store. Its type is read from its bytes; a type the client claims plays no part.</summary>
/// <param name="FileName">The name of the file; a directory in it is dropped.</param>
/// <param name="Content">The bytes, read once to their end. The caller disposes the stream.</param>
public sealed record MediaUploadFile(string FileName, Stream Content);

/// <summary>
/// Changes to the metadata of an asset. A member left null stays as it is; an empty text clears the value. The alternative
/// text and caption are those of the locale of the operation.
/// </summary>
public sealed record MediaAssetChanges
{
    /// <summary>The title.</summary>
    public string? Title { get; init; }

    /// <summary>The alternative text in the locale of the operation.</summary>
    public string? Alt { get; init; }

    /// <summary>The caption in the locale of the operation.</summary>
    public string? Caption { get; init; }

    /// <summary>Who to credit.</summary>
    public string? Credit { get; init; }

    /// <summary>The tags, which replace those of the asset.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>The focal point.</summary>
    public MediaFocalPoint? FocalPoint { get; init; }

    /// <summary>Whether the asset is private.</summary>
    public bool? IsPrivate { get; init; }
}

/// <summary>What assets to list. The filters add up.</summary>
public sealed record MediaQuery
{
    /// <summary>The folder whose assets to list, or <see langword="null"/> for every folder the caller may read.</summary>
    public Guid? FolderId { get; init; }

    /// <summary>Whether to list the assets at the root only, outside every folder. It cannot be combined with <see cref="FolderId"/>.</summary>
    public bool RootOnly { get; init; }

    /// <summary>Whether <see cref="FolderId"/> takes in the assets of its subfolders too.</summary>
    public bool IncludeSubfolders { get; init; }

    /// <summary>A MIME type, such as <c>image/png</c>, or a family, such as <c>image/*</c>.</summary>
    public string? MimeType { get; init; }

    /// <summary>A tag that the assets carry.</summary>
    public string? Tag { get; init; }

    /// <summary>A text of 1 to 256 characters that the title, the file name or a tag of the assets contains, whatever its case.</summary>
    public string? Search { get; init; }

    /// <summary>The page, from 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>The size of a page, from 1 to 100.</summary>
    public int PageSize { get; init; } = 50;
}
