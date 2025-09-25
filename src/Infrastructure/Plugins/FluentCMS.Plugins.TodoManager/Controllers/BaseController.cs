using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Plugins.TodoManager.Controllers;

[ApiController]
[Produces("application/json")]
//[TypeFilter(typeof(ApiTokenAuthorizeFilter))]
[Route("api/[controller]/[action]")]
public abstract class BaseController
{
    public static ApiResult<T> Ok<T>(T item)
    {
        return new ApiResult<T>(item);
    }

    public static ApiResult Ok()
    {
        return new ApiResult();
    }

    public static ApiPagedResult<T> OkPaged<T>(IEnumerable<T> items, long totalCount, int pageNumber, int pageSize)
    {
        return new ApiPagedResult<T>(items, totalCount, pageNumber, pageSize);
    }

    //public static ApiPagedResult<T> Ok<T>(IList<T> items)
    //{
    //    return new ApiPagedResult<T>(items, items.Count, 1, items.Count);
    //}
    public static ApiPagedResult<T> OkPaged<T>(ICollection<T> items)
    {
        return new ApiPagedResult<T>(items, items.Count, 1, items.Count);
    }
    //public static ApiPagedResult<T> Ok<T>(List<T> items)
    //{
    //    return new ApiPagedResult<T>(items, items.Count, 1, items.Count);
    //}
}