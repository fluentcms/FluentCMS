using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Controllers;

[Route("api/[controller]/[action]/")]
[ApiController]
[Produces("application/json")]
public abstract class BaseController : ControllerBase
{

}