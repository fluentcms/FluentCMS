using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.API.Controllers;

[Route("API/[controller]/[action]/")]
[ApiController]
[Produces("application/json")]
public class BaseController : ControllerBase
{
   
}
