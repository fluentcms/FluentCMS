using FluentCMS.Web.Api.Setup;

namespace FluentCMS.Web.Api.Controllers;

public class SetupController(ISetupManager setupManager) : BaseGlobalController
{
    public const string AREA = "Setup Management";

    [HttpGet]
    [PolicyAll]
    public async Task<IApiResult<bool>> IsInitialized()
    {
        return Ok(await setupManager.IsInitialized());
    }

    [HttpPost]
    [PolicyAll]
    public async Task<IApiResult<bool>> Start(SetupRequest request)
    {
        return Ok(await setupManager.Start(request));
    }
}
