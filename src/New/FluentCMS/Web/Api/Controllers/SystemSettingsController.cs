using FluentCMS.Web.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

public class SystemSettingsController(ISystemSettingsService systemSettingsService) : BaseController
{
    [HttpGet]
    public async Task<IApiResult<SystemSettings>> Get(CancellationToken cancellationToken = default)
    {
        var host = await systemSettingsService.Get(cancellationToken);
        return new ApiResult<SystemSettings>(host);
    }

    [HttpPut]
    public async Task<IApiResult<SystemSettings>> Update(SystemSettingsUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var systemSettings = new SystemSettings { SuperUsers = request.SuperUsers };
        var updated = await systemSettingsService.Update(systemSettings, cancellationToken);
        return new ApiResult<SystemSettings>(updated);
    }
}
