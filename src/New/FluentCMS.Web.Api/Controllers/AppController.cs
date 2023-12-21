using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[Route("app/{appSlug}/api/[controller]/[action]")]
public class AppController(
    IAppService appService,
    IMapper mapper) : BaseController
{
    [HttpGet]
    public async Task<IApiResult<AppResponse>> Get([FromRoute] string appSlug, CancellationToken cancellationToken = default)
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

    //[HttpGet]
    //public async Task<IApiPagingResult<AppResponse>> GetAllApps(CancellationToken cancellationToken = default)
    //{
    //    var apps = await appService.GetAll(cancellationToken);
    //    var appsResponse = mapper.Map<List<AppResponse>>(apps);
    //    return OkPaged(appsResponse);
    //}



    //[HttpPost]
    //public async Task<IApiResult<AppResponse>> CreateApp(AppCreateRequest request, CancellationToken cancellationToken = default)
    //{
    //    var app = mapper.Map<App>(request);
    //    var created = await appService.Create(app, cancellationToken);
    //    var response = mapper.Map<AppResponse>(created);
    //    return Ok(response);
    //}

    //[HttpDelete("{appId}")]
    //public async Task<IApiResult<bool>> DeleteApp([FromRoute] Guid appId, CancellationToken cancellationToken = default)
    //{
    //    _ = await appService.Delete(appId, cancellationToken);
    //    return Ok(true);
    //}
}
