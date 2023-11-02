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
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            CreatedBy = "",
            LastUpdatedBy = "",
            Name = request.Name,
            Username = request.Username,
            Password = request.Password,
            UserRoles = request.Roles?.Select(r => new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = r,
            }).ToList() ?? [],
        };
        await _userService.Create(user, cancellationToken);
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
        user.UserRoles = request.Roles?.Select(r => new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = r,
        }).ToList() ?? [];

        await _userService.Update(user);
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userService.GetById(request.Id);
        await _userService.Delete(user);
    }
}
