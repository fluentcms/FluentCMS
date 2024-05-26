using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

[ApiController]
[Produces("application/json")]
// TODO: Uncomment the following line to enable JWT authorization
// after resolving API client issue in server interactive render mode
//[JwtAuthorize]
[AllowAnonymous]
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
