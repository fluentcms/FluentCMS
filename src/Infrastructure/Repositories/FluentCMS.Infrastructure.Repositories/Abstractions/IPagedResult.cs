namespace FluentCMS.Infrastructure.Repositories.Abstractions;

/// <summary>
/// Pagination result wrapper
/// </summary>
public interface IPagedResult<T>
{
    IEnumerable<T> Items { get; }
    long TotalCount { get; }
    int Page { get; }
    int PageSize { get; }
    int TotalPages { get; }
    bool HasNextPage { get; }
    bool HasPreviousPage { get; }
}
