using FluentCMS.Application.Users;
using FluentCMS.Entities.Users;
using FluentCMS.Models;
using FluentCMS.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Shared.Controllers;
public class RolesController(MediatR.IMediator mediator) : BaseController
{
    [HttpGet]
    public async Task<ActionResult<ApiResult<IEnumerable<Role>>>> GetAll()
    {
        var data = await mediator.Send(new GetRolesQuery());
        return SuccessResult(data);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<Role?>>> GetById([FromRoute] Guid id)
    {
        var data = await mediator.Send(new GetRolesQuery());
        return SuccessResult(data.FirstOrDefault(x => x.Id == id));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<bool>>> Create(CreateRoleCommand request)
    {
        await mediator.Send(request);
        return SuccessResult(true);
    }

    [HttpPut]
    public async Task<ActionResult<ApiResult<bool>>> Edit(EditRoleCommand request)
    {
        await mediator.Send(request);
        return SuccessResult(true);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResult<bool>>> Delete([FromRoute] DeleteRoleCommand request)
    {
        await mediator.Send(request);
        return SuccessResult(true);
    }
}
