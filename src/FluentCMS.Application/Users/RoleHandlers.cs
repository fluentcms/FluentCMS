using FluentCMS.Entities.Users;
using FluentCMS.Services;
using MediatR;

namespace FluentCMS.Application.Users;
internal class RoleHandlers :
    IRequestHandler<GetRolesQuery, IEnumerable<Role>>,
    IRequestHandler<CreateRoleCommand, Guid>,
    IRequestHandler<EditRoleCommand>,
    IRequestHandler<DeleteRoleCommand>
{
    private readonly RoleService _roleService;

    public RoleHandlers(RoleService roleService)
    {
        _roleService = roleService;
    }

    public async Task<IEnumerable<Role>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await _roleService.GetAll();
        return roles;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            CreatedBy = "",
            LastUpdatedBy = "",
            Name = request.Name,
            Description = request.Description,
            AutoAssigned = request.AutoAssigned,
            SiteId = request.SiteId,
        };
        await _roleService.Create(role);
        return role.Id;
    }

    public async Task Handle(EditRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await _roleService.GetById(request.Id);
        user.LastUpdatedAt = DateTime.UtcNow;
        user.LastUpdatedBy = "";
        user.Name = request.Name;
        user.Description = request.Description;
        user.AutoAssigned = request.AutoAssigned;
        user.SiteId = request.SiteId;

        await _roleService.Update(user);
    }

    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await _roleService.GetById(request.Id);
        await _roleService.Delete(user);
    }
}
