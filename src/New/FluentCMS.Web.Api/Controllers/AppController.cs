using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

public class AppController(IAppService appService, IMapper mapper) : BaseSystemController
{

    [HttpPost]
    public async Task<IApiResult<AppResponse>> Create([FromBody] AppCreateRequest request, CancellationToken cancellationToken = default)
    {
        var app = mapper.Map<App>(request);
        var created = await appService.Create(app, cancellationToken);
        var response = mapper.Map<AppResponse>(created);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IApiResult<AppResponse>> Update([FromBody] AppUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var app = mapper.Map<App>(request);
        var updated = await appService.Update(app, cancellationToken);
        var appResponse = mapper.Map<AppResponse>(updated);
        return Ok(appResponse);
    }

    [HttpGet]
    public async Task<IApiPagingResult<AppResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var apps = await appService.GetAll(cancellationToken);
        var appsResponse = mapper.Map<List<AppResponse>>(apps);
        return OkPaged(appsResponse);
    }

    [HttpDelete("{appId}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid appId, CancellationToken cancellationToken = default)
    {
        await appService.Delete(appId, cancellationToken);
        return Ok(true);
    }

    [HttpGet("{appSlug}")]
    public async Task<IApiResult<AppResponse>> GetBySlug([FromRoute] string appSlug, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var appResponse = mapper.Map<AppResponse>(app);
        return Ok(appResponse);
    }
}
