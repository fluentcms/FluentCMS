using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Produces("application/json")]
public abstract class BaseController
{
    public ApiResult<T> Ok<T>(T item)
    {
        return new ApiResult<T>(item);
    }

    public ApiPagingResult<T> OkPaged<T>(IEnumerable<T> items)
    {
        return new ApiPagingResult<T>(items);
    }
}
