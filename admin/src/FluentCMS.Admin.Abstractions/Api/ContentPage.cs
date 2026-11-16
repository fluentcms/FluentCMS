namespace FluentCMS.Admin.Api;

/// <summary>A page of a content list, from <c>GET /content/{type}</c>.</summary>
public sealed record ContentPage
{
    /// <summary>The entries of the page.</summary>
    public required IReadOnlyList<ContentEntry> Items { get; init; }

    /// <summary>The number of entries that match, or <see langword="null"/> when they were not counted.</summary>
    public long? Total { get; init; }

    /// <summary>The number of the page, from 1.</summary>
    public int? Page { get; init; }

    /// <summary>The size of a page.</summary>
    public required int PageSize { get; init; }

    /// <summary>The cursor of the next page, when there is one.</summary>
    public string? NextCursor { get; init; }
}
