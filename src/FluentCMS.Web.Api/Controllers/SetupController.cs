using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

[AllowAnonymous]
public class SetupController(SetupManager setupManager) : BaseGlobalController
{
    [HttpGet]
    public async Task<IApiResult<bool>> IsInitialized()
    {
        return Ok(await setupManager.IsInitialized());
    }

    [HttpPost]
    public async Task<IApiResult<bool>> Start(SetupRequest request)
    {
        return Ok(await setupManager.Start(request));
    }
}
