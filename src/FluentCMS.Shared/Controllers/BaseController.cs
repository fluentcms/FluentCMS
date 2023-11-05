using FluentCMS.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Controllers;

[ApiController]
[Route("api/[controller]/")]
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
