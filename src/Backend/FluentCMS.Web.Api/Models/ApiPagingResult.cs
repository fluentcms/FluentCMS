namespace FluentCMS.Web.Api.Models;

/// <summary>
/// Represents a paginated result of an API call, with metadata about the pagination.
/// </summary>
/// <typeparam name="TData">The type of the data contained in the paginated result.</typeparam>
public interface IApiPagingResult<TData> : IApiResult<IEnumerable<TData>>
{
    /// <summary>
    /// Gets the number of items per page.
    /// </summary>
    int PageSize { get; }

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    int TotalPages { get; }

    /// <summary>
    /// Gets the current page number.
    /// </summary>
    int PageNumber { get; }

    /// <summary>
    /// Gets the total number of items across all pages.
    /// </summary>
    long TotalCount { get; }

    /// <summary>
    /// Gets a value indicating whether there is a previous page.
    /// </summary>
    bool HasPrevious { get; }

    /// <summary>
    /// Gets a value indicating whether there is a next page.
    /// </summary>
    bool HasNext { get; }
}

/// <summary>
/// Provides a concrete implementation of the <see cref="IApiPagingResult{TData}"/> interface.
/// </summary>
/// <typeparam name="TData">The type of the data contained in the paginated result.</typeparam>
public class ApiPagingResult<TData> : ApiResult<IEnumerable<TData>>, IApiPagingResult<TData>
{
    /// <inheritdoc />
    public int PageSize { get; }

    /// <inheritdoc />
    public int TotalPages { get; }

    /// <inheritdoc />
    public int PageNumber { get; }

    /// <inheritdoc />
    public long TotalCount { get; }

    /// <inheritdoc />
    public bool HasPrevious { get; }

    /// <inheritdoc />
    public bool HasNext { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiPagingResult{TData}"/> class with the specified data.
    /// </summary>
    /// <param name="data">The paginated data.</param>
    public ApiPagingResult(IEnumerable<TData> data) : base(data)
    {
        // Initialize properties based on the provided data.
        PageNumber = 1; // Assuming default value for demonstration.
        PageSize = data.Count();
        TotalCount = data.Count();
        TotalPages = (int)Math.Ceiling((double)TotalCount / PageSize);
        HasPrevious = PageNumber > 1;
        HasNext = PageNumber < TotalPages;
    }
}
