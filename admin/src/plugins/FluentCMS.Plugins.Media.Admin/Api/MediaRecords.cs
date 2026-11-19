using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.Media.Admin.Api;

// The records of the media routes the half reads, its own rather than the server half's (spec 5.1), which the API tests
// hold to the reference host's OpenAPI document (spec 7). Each reads the members the library shows and skips the others.
public sealed record MediaAssetInfo
{
    public required Guid Id { get; init; }

    public Guid? FolderId { get; init; }

    public required string FileName { get; init; }

    public required string MimeType { get; init; }

    public required long Size { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? Title { get; init; }

    // The locale of Alt and Caption: the one the asset was read in, which a save of them writes to.
    public string? Locale { get; init; }

    public string? Alt { get; init; }

    public string? Caption { get; init; }

    public string? Credit { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public MediaPoint? FocalPoint { get; init; }

    public bool IsPrivate { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    // The ETag of the read or the save the asset came from, which the next save sends back as If-Match.
    [JsonIgnore]
    public string? ETag { get; init; }

    public required MediaAssetUrls Urls { get; init; }

    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(Title) ? FileName : Title;
}

public sealed record MediaAssetUrls
{
    public required string Original { get; init; }

    public string? Thumbnail { get; init; }
}

// A point of an image as fractions of its width and height from the top left corner, each from 0 to 1.
public sealed record MediaPoint
{
    public double X { get; init; }

    public double Y { get; init; }
}

internal sealed record MediaAssetPage
{
    public required IReadOnlyList<MediaAssetInfo> Items { get; init; }

    public required long Total { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed record MediaFolderInfo
{
    public required Guid Id { get; init; }

    public Guid? ParentId { get; init; }

    public required string Name { get; init; }
}

// An asset as an upload or a move answers it, without its URLs.
internal sealed record MediaAssetSaved
{
    public required Guid Id { get; init; }

    public Guid? FolderId { get; init; }

    public required string FileName { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    // The ETag of a move's answer.
    [JsonIgnore]
    public string? ETag { get; init; }
}

// An asset as a change of its metadata or a replace answers it: the metadata without the URLs.
internal sealed record MediaAssetChanged
{
    public required Guid Id { get; init; }

    public required string FileName { get; init; }

    public required string MimeType { get; init; }

    public required long Size { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? Title { get; init; }

    public string? Locale { get; init; }

    public string? Alt { get; init; }

    public string? Caption { get; init; }

    public string? Credit { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public MediaPoint? FocalPoint { get; init; }

    public bool IsPrivate { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

internal sealed record MediaUsageInfo
{
    public required string Type { get; init; }

    public required Guid EntryId { get; init; }

    public required string Status { get; init; }

    public string? Locale { get; init; }

    public required string Field { get; init; }
}

internal sealed record MediaDownloadInfo
{
    public required string Url { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

// A preset of the site: its name and the parameters it stores, with enum values as camel case names.
internal sealed record MediaPresetInfo
{
    public required string Name { get; init; }

    public required MediaPresetOptions Options { get; init; }
}

internal sealed record MediaPresetOptions
{
    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? Fit { get; init; }

    public int? Quality { get; init; }

    public string? Format { get; init; }
}

// The types of the schema as far as the labels of the entries that use an asset need them.
internal sealed record MediaSchema
{
    public required IReadOnlyList<MediaSchemaType> Types { get; init; }
}

internal sealed record MediaSchemaType
{
    public required string Name { get; init; }

    public bool Localized { get; init; }

    public string? DisplayName { get; init; }

    public string? TitleField { get; init; }

    public required IReadOnlyList<MediaSchemaField> Fields { get; init; }
}

internal sealed record MediaSchemaField
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool Multiple { get; init; }

    public bool Hidden { get; init; }
}

// An asset of a media field as a content read populates it (the media plugin's MediaFieldType), which a list cell shows. Its
// URLs are those of the Delivery API: none for a private asset, and no thumbnail.
internal sealed record MediaFieldAsset
{
    public required Guid Id { get; init; }

    public string? FileName { get; init; }

    public string? MimeType { get; init; }

    public string? Title { get; init; }

    public MediaFieldUrls? Urls { get; init; }

    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(Title) ? FileName ?? Id.ToString("D") : Title;
}

internal sealed record MediaFieldUrls
{
    public string? Original { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MediaAssetInfo))]
[JsonSerializable(typeof(MediaFieldAsset))]
[JsonSerializable(typeof(MediaAssetChanged))]
[JsonSerializable(typeof(ItemList<MediaUsageInfo>))]
[JsonSerializable(typeof(MediaDownloadInfo))]
[JsonSerializable(typeof(MediaPresetInfo))]
[JsonSerializable(typeof(ItemList<MediaPresetInfo>))]
[JsonSerializable(typeof(MediaSchema))]
[JsonSerializable(typeof(ItemList<JsonObject>))]
[JsonSerializable(typeof(MediaAssetPage))]
[JsonSerializable(typeof(MediaAssetSaved))]
[JsonSerializable(typeof(ItemList<MediaAssetSaved>))]
[JsonSerializable(typeof(MediaFolderInfo))]
[JsonSerializable(typeof(ItemList<MediaFolderInfo>))]
internal sealed partial class MediaJsonContext : JsonSerializerContext;
