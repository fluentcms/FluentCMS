using FluentCMS.Api.Core.Api.Filters;

namespace FluentCMS.Api.Core.Api.Controllers;

[ApiController]
[Produces("application/json")]
[Route("api/[controller]/[action]")]
[TypeFilter(typeof(ApiTokenAuthorizeFilter))]
public abstract class BaseController
{
}
