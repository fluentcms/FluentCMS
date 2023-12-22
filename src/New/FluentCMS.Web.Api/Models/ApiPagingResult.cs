namespace FluentCMS.Web.Api.Models;

/// <summary>
/// Represents a paginated API response with a collection of data.
/// </summary>
/// <typeparam name="TData">The type of data in the collection.</typeparam>
public interface IApiPagingResult<TData> : IApiResult<IEnumerable<TData>>
{
    /// <summary>
    /// The size of each page.
    /// </summary>
    int PageSize { get; }

    /// <summary>
    /// The total number of pages.
    /// </summary>
    int TotalPages { get; }

    /// <summary>
    /// The current page number.
    /// </summary>
    int PageNumber { get; }

    /// <summary>
    /// The total count of items in all pages.
    /// </summary>
    long TotalCount { get; }

    /// <summary>
    /// Indicates whether there is a previous page.
    /// </summary>
    bool HasPrevious { get; }

    /// <summary>
    /// Indicates whether there is a next page.
    /// </summary>
    bool HasNext { get; }
}

/// <summary>
/// A concrete implementation of a paginated API response.
/// </summary>
/// <typeparam name="TData">The type of data in the collection.</typeparam>
public class ApiPagingResult<TData> : ApiResult<IEnumerable<TData>>, IApiPagingResult<TData>
{
    /// <summary>
    /// The size of each page.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    /// The total number of pages.
    /// </summary>
    public int TotalPages { get; }

    /// <summary>
    /// The current page number.
    /// </summary>
    public int PageNumber { get; }

    /// <summary>
    /// The total count of items in all pages.
    /// </summary>
    public long TotalCount { get; }

    /// <summary>
    /// Indicates whether there is a previous page.
    /// </summary>
    public bool HasPrevious { get; }

    /// <summary>
    /// Indicates whether there is a next page.
    /// </summary>
    public bool HasNext { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiPagingResult{TData}"/> class with specified data.
    /// </summary>
    /// <param name="data">The data for the paginated response.</param>
    public ApiPagingResult(IEnumerable<TData> data) : base(data)
    {
        // Initialize properties based on the data
        PageNumber = 1;
        PageSize = data.Count();
        TotalCount = data.Count();
        //TotalPages = CalculateTotalPages(data.Count());
        HasPrevious = PageNumber > 1;
        HasNext = PageNumber < TotalPages;
    }
}
