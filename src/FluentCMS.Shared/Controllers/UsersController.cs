using FluentCMS.Application.Users;
using FluentCMS.Entities;
using FluentCMS.Models;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace FluentCMS.Web.Controllers;
public class UsersController : BaseController
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<IEnumerable<User>>>> GetUsers()
    {
        var data = await _mediator.Send(new GetUsersQuery());
        return SuccessResult(data);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<User?>>> GetUserById([FromRoute] Guid id)
    {
        var data = await _mediator.Send(new GetUserByIdQuery { Id = id });
        return SuccessResult(data);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<Guid>>> CreateUser(CreateUserCommand request)
    {
        var result = await _mediator.Send(request);
        return SuccessResult(result);
    }

    [HttpPut]
    public async Task<ActionResult<ApiResult<bool>>> EditUser(EditUserCommand request)
    {
        await _mediator.Send(request);
        return SuccessResult(true);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResult<bool>>> DeleteUser([FromRoute] DeleteUserCommand request)
    {
        await _mediator.Send(request);
        return SuccessResult(true);
    }
}
