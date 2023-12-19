using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// API controller for managing host settings in the FluentCMS system.
/// Provides actions for retrieving and updating host information.
/// </summary>
public class HostController(IHostService hostService) : BaseController
{
    /// <summary>
    /// Retrieves the current host settings.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The current host settings.</returns>
    [HttpGet]
    public async Task<IApiResult<Host>> Get(CancellationToken cancellationToken = default)
    {
        var host = await hostService.Get(cancellationToken);
        return new ApiResult<Host>(host);
    }

    /// <summary>
    /// Updates the host settings.
    /// </summary>
    /// <param name="request">The host update request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The updated host settings.</returns>
    [HttpPut]
    public async Task<IApiResult<Host>> Update(HostUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var host = await hostService.Get(cancellationToken);

        host.SuperUsers = request.SuperUsers;

        var updateHost = await hostService.Update(host, cancellationToken);
        return new ApiResult<Host>(updateHost);
    }
}
