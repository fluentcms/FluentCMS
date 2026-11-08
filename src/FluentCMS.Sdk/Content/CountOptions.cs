namespace FluentCMS.Content;

/// <summary>Which entries <see cref="IContentService.CountAsync"/> counts.</summary>
public sealed record CountOptions
{
    /// <summary>The filter, or <see langword="null"/> for every entry.</summary>
    public ContentFilter? Filter { get; init; }

    /// <summary>Which state of the entries to count.</summary>
    public QueryStatus Status { get; init; } = QueryStatus.Published;
}
