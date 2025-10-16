using FluentCMS.Api.Core.Api.Filters;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Core.Api.Controllers;

[ApiController]
[Produces("application/json")]
[Route("api/[controller]/[action]")]
[TypeFilter(typeof(ApiTokenAuthorizeFilter))]
public abstract class BaseController
{
}
