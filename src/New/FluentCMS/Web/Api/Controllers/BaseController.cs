using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Produces("application/json")]
public abstract class BaseController
{
    // Common properties and methods for all controllers can be defined here.
}
