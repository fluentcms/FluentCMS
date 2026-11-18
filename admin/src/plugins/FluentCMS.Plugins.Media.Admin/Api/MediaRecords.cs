using System.Text.Json;
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

    public string? Alt { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public required MediaAssetUrls Urls { get; init; }

    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(Title) ? FileName : Title;
}

public sealed record MediaAssetUrls
{
    public required string Original { get; init; }

    public string? Thumbnail { get; init; }
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
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MediaAssetPage))]
[JsonSerializable(typeof(MediaAssetSaved))]
[JsonSerializable(typeof(ItemList<MediaAssetSaved>))]
[JsonSerializable(typeof(MediaFolderInfo))]
[JsonSerializable(typeof(ItemList<MediaFolderInfo>))]
internal sealed partial class MediaJsonContext : JsonSerializerContext;
