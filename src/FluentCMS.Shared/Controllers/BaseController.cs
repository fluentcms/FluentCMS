using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Controllers;

[Route("api/[controller]/[action]/")]
[ApiController]
[Produces("application/json")]
public abstract class BaseController : ControllerBase
{

}