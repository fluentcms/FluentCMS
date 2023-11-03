using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Server.Controllers;

[Route("api/[controller]/[action]")]
[ApiController]
[Produces("application/json")]
public abstract class BaseController 
{

}
