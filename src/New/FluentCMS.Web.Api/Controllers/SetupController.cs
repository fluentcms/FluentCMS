using FluentCMS.Web.Api.Setup;

namespace FluentCMS.Web.Api.Controllers;

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

    [HttpPost]
    public async Task<IApiResult<bool>> Reset()
    {
        await setupManager.Reset();
        return Ok(await setupManager.IsInitialized());
    }
}
