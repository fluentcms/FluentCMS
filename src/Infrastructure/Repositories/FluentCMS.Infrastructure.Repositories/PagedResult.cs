namespace FluentCMS.Infrastructure.Repositories;

/// <summary>
/// Implementation of pagination result wrapper
/// </summary>
public class PagedResult<T>(IEnumerable<T> items, long totalCount, int page, int pageSize) : IPagedResult<T>
{
    public IEnumerable<T> Items { get; init; } = items;
    public long TotalCount { get; init; } = totalCount;
    public int Page { get; init; } = page;
    public int PageSize { get; init; } = pageSize;

    // Calculate total pages based on total count and page size
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    // Check if there's a next page available
    public bool HasNextPage => Page < TotalPages;

    // Check if there's a previous page available
    public bool HasPreviousPage => Page > 1;
}
