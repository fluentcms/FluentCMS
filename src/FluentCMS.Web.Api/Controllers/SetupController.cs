using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Web.Api.Controllers;

[AllowAnonymous]
public class SetupController(SetupManager setupManager, IHostEnvironment hostEnvironment) : BaseGlobalController
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
        if (!hostEnvironment.IsStaging())
            throw new AppException(ExceptionCodes.SetupSettingsOnlyAvailableOnStagingEnvironment);

        await setupManager.Reset();
        return Ok(await setupManager.IsInitialized());
    }

    [HttpGet]
    public async Task<IApiResult<PageFullDetailResponse>> GetSetupPage()
    {
        await setupManager.Reset();
        return Ok(await setupManager.GetSetupPage());
    }
}
