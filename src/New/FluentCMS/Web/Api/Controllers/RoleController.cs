using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[Route("{appSlug}/api/[controller]/[action]")]
public class RoleController(
    IMapper mapper,
    IRoleService roleService,
    AppService appService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<RoleResponse>> GetAll([FromRoute] string appSlug, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var roles = await roleService.GetAll(app.Id, cancellationToken);
        var roleResponses = mapper.Map<IEnumerable<RoleResponse>>(roles);
        return OkPaged(roleResponses);
    }

    [HttpPost]
    public async Task<IApiResult<RoleResponse>> Create([FromRoute] string appSlug, [FromBody] RoleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        role.AppId = app.Id;
        var newRole = await roleService.Create(role, cancellationToken);
        var roleResponse = mapper.Map<RoleResponse>(newRole);
        return Ok(roleResponse);
    }

    [HttpPut]
    public async Task<IApiResult<RoleResponse>> Update([FromRoute] string appSlug, [FromBody] RoleUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var role = mapper.Map<Role>(request);
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        role.AppId = app.Id;
        var updated = await roleService.Update(role, cancellationToken);
        var roleResponse = mapper.Map<RoleResponse>(updated);
        return Ok(roleResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string appSlug, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        await roleService.Delete(app.Id, id, cancellationToken);
        return Ok(true);
    }
}
