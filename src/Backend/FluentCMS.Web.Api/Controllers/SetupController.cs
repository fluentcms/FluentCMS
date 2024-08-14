using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace FluentCMS.Web.Api.Controllers;

public class SetupController(ISetupManager setupManager, IHttpContextAccessor httpContextAccessor) : BaseGlobalController
{
    public const string AREA = "Setup Management";
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    [HttpGet]
    [AnyPolicy]
    public async Task<IApiResult<bool>> IsInitialized()
    {
        return Ok(await setupManager.IsInitialized());
    }

    [HttpPost]
    [AnyPolicy]
    public async Task<IApiResult<bool>> Start(SetupRequest request)
    {
        var host = $"{_httpContextAccessor.HttpContext!.Request.Scheme}://{_httpContextAccessor.HttpContext!.Request.Host}";
        return Ok(await setupManager.Start(request, host));
    }
}
