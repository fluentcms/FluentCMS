using FluentCMS.Api.Filters;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

[ApiController]
[Produces("application/json")]
[Route("api/[controller]/[action]")]
[TypeFilter(typeof(ApiTokenAuthorizeFilter))]
public abstract class BaseController
{
}
