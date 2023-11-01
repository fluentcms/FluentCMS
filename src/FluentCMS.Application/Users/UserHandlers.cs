using FluentCMS.Entities.Users;
using FluentCMS.Services;
using MediatR;

namespace FluentCMS.Application.Users;
internal class UserHandlers :
    IRequestHandler<GetUsersQuery, IEnumerable<User>>,
    IRequestHandler<GetUserByIdQuery, User?>,
    IRequestHandler<CreateUserCommand, Guid>,
    IRequestHandler<EditUserCommand>,
    IRequestHandler<DeleteUserCommand>
{
    private readonly UserService _userService;

    public UserHandlers(UserService userService)
    {
        _userService = userService;
    }

    public async Task<IEnumerable<User>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _userService.GetAll();
        return users;
    }

    public async Task<User?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await _userService.GetById(request.Id);
        return user;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            CreatedBy = "",
            LastUpdatedBy = "",
            Name = request.Name,
            Username = request.Username,
            Password = request.Password,
        };
        await _userService.Create(user, request.Roles.AsEnumerable(), cancellationToken);
        return user.Id;
    }

    public async Task Handle(EditUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userService.GetById(request.Id);
        user.LastUpdatedAt = DateTime.UtcNow;
        user.LastUpdatedBy = "";
        user.Name = request.Name;
        user.Username = request.Username;
        user.Password = request.Password;

        await _userService.Update(user, request.Roles.AsEnumerable());
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userService.GetById(request.Id);
        await _userService.Delete(user);
    }
}
