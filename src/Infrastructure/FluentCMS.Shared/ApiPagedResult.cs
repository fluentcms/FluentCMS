namespace FluentCMS;

public interface IApiPagedResult
{
    bool HasNext { get; }
    bool HasPrevious { get; }
    int PageNumber { get; }
    long PageSize { get; }
    long TotalCount { get; }
    int TotalPages { get; }
}

public interface IApiPagedResult<TData> : IApiPagedResult, IApiResult<TData>
{
}

public class ApiPagedResult<TData> : ApiResult<IEnumerable<TData>>, IApiPagedResult
{
    public long PageSize { get; }
    public int TotalPages { get; }
    public int PageNumber { get; }
    public long TotalCount { get; }
    public bool HasPrevious { get; }
    public bool HasNext { get; }

    public ApiPagedResult(IEnumerable<TData> data)
    {
        var totalCount = data.Count();
        Data = data;
        PageNumber = 1;
        PageSize = totalCount;
        TotalCount = 0;
        TotalPages = totalCount;
        HasPrevious = false;
        HasNext = false;
    }

    public ApiPagedResult(IEnumerable<TData> data, long totalCount) : this(data, totalCount, 1, totalCount)
    {
    }

    public ApiPagedResult(IEnumerable<TData> data, long totalCount, int pageNumber, long pageSize) : base(data)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
        HasPrevious = PageNumber > 1;
        HasNext = PageNumber < TotalPages;
    }
}
