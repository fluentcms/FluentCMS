namespace FluentCMS.Web.Api.Controllers;

public class SetupController(SetupManager setupManager) : BaseGlobalController
{

    [HttpGet]
    public async Task<IApiResult<bool>> IsInitialized()
    {
        return Ok(await setupManager.IsInitialized());
    }

    [HttpPost]
    public async Task<IApiResult<GlobalSettings>> Start(SetupRequest request)
    {
        return Ok(await setupManager.Start(request.Username, request.Email, request.Password, request.AdminDomain));
    }

    [HttpPost]
    public async Task<IApiResult<bool>> Reset()
    {
        await setupManager.Reset();
        return Ok(await setupManager.IsInitialized());
    }
}
