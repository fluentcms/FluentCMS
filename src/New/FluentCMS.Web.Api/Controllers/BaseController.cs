using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[ApiController]
[Produces("application/json")]
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

[Route("api/app/{appSlug}/[controller]/[action]")]
public abstract class BaseAppController : BaseController
{

}

[Route("api/global/[controller]/[action]")]
public abstract class BaseGlobalController : BaseController
{

}
