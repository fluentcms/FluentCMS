using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// Serves as the base class for API controllers in the FluentCMS system.
/// This abstract class provides common configurations and functionalities shared by all derived controllers.
/// </summary>
/// <remarks>
/// This class is marked as abstract and is not intended to be instantiated directly.
/// It includes common API controller configurations such as routing and response formatting.
/// </remarks>
[ApiController]
[Route("api/[controller]/[action]")]
[Produces("application/json")]
public abstract class BaseController
{
    // Common properties and methods for all controllers can be defined here.
}
