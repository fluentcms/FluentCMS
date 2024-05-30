using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

public class SetupController(SetupManager setupManager) : BaseGlobalController
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IApiResult<bool>> IsInitialized()
    {
        return Ok(await setupManager.IsInitialized());
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IApiResult<bool>> Start(SetupRequest request)
    {
        return Ok(await setupManager.Start(request));
    }
}
