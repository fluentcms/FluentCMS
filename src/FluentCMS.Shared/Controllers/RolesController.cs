using FluentCMS.Application.Dtos;
using FluentCMS.Application.Dtos.Users;
using FluentCMS.Application.Services;
using FluentCMS.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Controllers;
public class RolesController(IRoleService roleService) : BaseController
{
    [HttpGet]
    public async Task<ActionResult<ApiResult<PagingResponse<RoleDto>>>> Search([FromQuery] SearchRoleRequest request)
    {
        var data = await roleService.Search(request);
        return SuccessResult(data);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<RoleDto>>> GetById([FromRoute] Guid id)
    {
        var data = await roleService.GetById(id);
        return SuccessResult(data);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<Guid>>> Create(CreateRoleRequest request)
    {
        var result = await roleService.Create(request);
        return SuccessResult(result);
    }

    [HttpPut]
    public async Task<ActionResult<ApiResult<bool>>> Edit(EditRoleRequest request)
    {
        await roleService.Edit(request);
        return SuccessResult(true);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResult<bool>>> Delete([FromRoute] DeleteRoleRequest request)
    {
        await roleService.Delete(request);
        return SuccessResult(true);
    }
}
