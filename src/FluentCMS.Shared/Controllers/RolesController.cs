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

    [HttpPost]
    public async Task<ActionResult<ApiResult<bool>>> Create(CreateRoleCommand request)
    {
        await mediator.Send(request);
        return SuccessResult(true);
    }
}
