using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

public class SetupController(ISetupManager setupManager) : BaseGlobalController
{
    public const string AREA = "Setup Management";
    public const string READ = "Read";
    public const string CREATE = "Create";

    [HttpGet]
    [Policy(AREA, READ)]
    [AllowAnonymous]
    public async Task<IApiResult<bool>> IsInitialized()
    {
        return Ok(await setupManager.IsInitialized());
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    [AllowAnonymous]
    public async Task<IApiResult<bool>> Start(SetupRequest request)
    {
        return Ok(await setupManager.Start(request));
    }
}
