namespace FluentCMS.Content;

/// <summary>A page of entries.</summary>
/// <typeparam name="T">The type of an entry.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>Creates a page.</summary>
    /// <param name="items">The entries.</param>
    /// <param name="total">How many entries match, or <see langword="null"/> when they were not counted.</param>
    /// <param name="page">The page number, or <see langword="null"/> for a page read by offset or cursor.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="nextCursor">The cursor of the next page, or <see langword="null"/> on the last page.</param>
    public PagedResult(IReadOnlyList<T> items, long? total, int? page, int pageSize, string? nextCursor)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items;
        Total = total;
        Page = page;
        PageSize = pageSize;
        NextCursor = nextCursor;
    }

    /// <summary>The entries.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>How many entries match, or <see langword="null"/> when they were not counted.</summary>
    public long? Total { get; }

    /// <summary>The page number, or <see langword="null"/> for a page read by offset or cursor.</summary>
    public int? Page { get; }

    /// <summary>The page size.</summary>
    public int PageSize { get; }

    /// <summary>The cursor of the next page, or <see langword="null"/> on the last page.</summary>
    public string? NextCursor { get; }
}
