using FluentCMS.Application.Users;
using FluentCMS.Entities;
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
    public async Task<ActionResult<IEnumerable<User>>> GetUsers()
    {
        var data = await _mediator.Send(new GetUsersQuery());
        return Ok(data);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<User?>> GetUserById([FromRoute] Guid id)
    {
        var data = await _mediator.Send(new GetUserByIdQuery { Id = id });
        return Ok(data);
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> CreateUser(CreateUserCommand request)
    {
        var result = await _mediator.Send(request);
        return Ok(result);
    }
}
