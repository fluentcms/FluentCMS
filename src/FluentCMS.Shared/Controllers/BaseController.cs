using FluentCMS.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Controllers;

[Route("api/[controller]/")]
[ApiController]
[Produces("application/json")]
public abstract class BaseController : ControllerBase
{

    public ApiResult<TData> SuccessResult<TData>(TData? data)
    {
        return new ApiResult<TData>
        {
            Data = data,
        };
    }
}
