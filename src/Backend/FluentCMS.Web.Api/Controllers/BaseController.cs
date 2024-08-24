using FluentCMS.Web.Api.Filters;

namespace FluentCMS.Web.Api.Controllers;

[ApiController]
[Produces("application/json")]
[JwtAuthorize]
[TypeFilter(typeof(ApiTokenAuthorizeFilter))]
public abstract class BaseController
{
    public static ApiResult<T> Ok<T>(T item)
    {
        return new ApiResult<T>(item);
    }

    public static ApiPagingResult<T> OkPaged<T>(IEnumerable<T> items)
    {
        return new ApiPagingResult<T>(items);
    }
}

[Route("api/[controller]/[action]")]
public abstract class BaseGlobalController : BaseController
{
}
