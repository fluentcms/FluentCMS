namespace FluentCMS.Web.Api.Models;

public interface IApiPagingResult<TData> : IApiResult<IEnumerable<TData>>
{
    int PageSize { get; }
    int TotalPages { get; }
    int PageNumber { get; }
    long TotalCount { get; }
    bool HasPrevious { get; }
    bool HasNext { get; }
}

public class ApiPagingResult<TData> : ApiResult<IEnumerable<TData>>, IApiPagingResult<TData>
{
    public int PageSize { get; }
    public int TotalPages { get; }
    public int PageNumber { get; }
    public long TotalCount { get; }
    public bool HasPrevious { get; }
    public bool HasNext { get; }
    public ApiPagingResult(IEnumerable<TData> data) : base(data)
    {
        // Initialize properties based on the data
        PageNumber = 1;
        PageSize = data.Count();
        TotalCount = data.Count();
        HasPrevious = PageNumber > 1;
        HasNext = PageNumber < TotalPages;
    }
}
