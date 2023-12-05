namespace FluentCMS.Api.Models;

public interface IApiPagingResult<TData> : IApiResult<IEnumerable<TData>>
{
    public int PageSize { get; }
    public int TotalPages { get; }
    public int PageNumber { get; }
    public long TotalCount { get; }
    public bool HasPrevious { get; }
    public bool HasNext { get; }
}

public class ApiPagingResult<TData> : ApiResult<IEnumerable<TData>>, IApiPagingResult<TData>
{
    public int PageSize { get; }
    public int TotalPages { get; }
    public int PageNumber { get; }
    public long TotalCount { get; }
    public bool HasPrevious { get; }
    public bool HasNext { get; }

    //public ListResult(IEnumerable<TData> data, int pageNumber, int pageSize, long totalCount)
    //{
    //    Data = data;
    //    PageNumber = pageNumber;
    //    PageSize = pageSize;
    //    TotalCount = totalCount;
    //    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
    //    HasPrevious = PageNumber > 1;
    //    HasNext = PageNumber < TotalPages;
    //}

    //public ListResult(IEnumerable<TData> data, int pageNumber, int pageSize, long totalCount, int totalPages, bool hasPrevious, bool hasNext)
    //{
    //    Data = data;
    //    PageNumber = pageNumber;
    //    PageSize = pageSize;
    //    TotalCount = totalCount;
    //    TotalPages = totalPages;
    //    HasPrevious = hasPrevious;
    //    HasNext = hasNext;
    //}

    public ApiPagingResult(IEnumerable<TData> data)
    {
        Data = data;
        PageNumber = 1;
        PageSize = 1;
        TotalCount = data.Count();
        TotalPages = 1;
        HasPrevious = false;
        HasNext = false;
    }
}