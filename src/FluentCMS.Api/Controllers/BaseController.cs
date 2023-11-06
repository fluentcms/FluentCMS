using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Produces("application/json")]
public abstract class BaseController
{
}
