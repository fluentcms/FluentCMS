using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class HostController(IHostService hostService) : BaseController
{
    [HttpGet]
    public async Task<IApiResult<Host>> Get(CancellationToken cancellationToken = default)
    {
        var host = await hostService.Get(cancellationToken);
        return new ApiResult<Host>(host);
    }

    [HttpPut]
    public async Task<IApiResult<Host>> Update(HostUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var host = new Host { SuperUsers = request.SuperUsers };
        var updateHost = await hostService.Update(host, cancellationToken);
        return new ApiResult<Host>(updateHost);
    }
}
