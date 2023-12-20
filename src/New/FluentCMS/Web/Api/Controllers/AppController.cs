using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[Route("{appSlug}/api/[controller]/[action]")]
public class AppController(
    IAppService appService,
    IMapper mapper) : BaseController
{
    [HttpGet]
    public async Task<IApiResult<AppResponse>> GetBySlug([FromRoute] string appSlug, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var appResponse = mapper.Map<AppResponse>(app);
        return Ok(appResponse);
    }

    [HttpPut]
    public async Task<IApiResult<AppResponse>> Update([FromRoute] string appSlug, [FromBody] AppUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var app = mapper.Map<App>(request);
        app.Slug = appSlug;
        var updated = await appService.Update(app, cancellationToken);
        var appResponse = mapper.Map<AppResponse>(updated);
        return Ok(appResponse);
    }
}
