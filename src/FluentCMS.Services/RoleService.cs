using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IRoleService
{
    Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default);
    Task<Role> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Edit(Role role, CancellationToken cancellationToken = default);
    Task Delete(Role role, CancellationToken cancellationToken = default);
}

internal class RoleService(IGenericRepository<Role> roleRepository) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default)
    {
        var roles = await roleRepository.GetAll(cancellationToken);
        return roles;
    }

    public async Task<Role> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var roles = await roleRepository.GetById(id, cancellationToken)
            ?? throw new ApplicationException("Requested role does not exists.");
        return roles;
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken = default)
    {
        if (role is null)
            throw new ApplicationException("role is not provided");

        var newRole = await roleRepository.Create(role, cancellationToken);
        return newRole ?? throw new ApplicationException("Role not created");
    }

    public async Task<Role> Edit(Role role, CancellationToken cancellationToken = default)
    {
        if (role == null)
            throw new ApplicationException("role is not provided");

        var updatedRole = await roleRepository.Update(role, cancellationToken);
        return updatedRole ?? throw new ApplicationException("Role not updated.");
    }

    public async Task Delete(Role role, CancellationToken cancellationToken = default)
    {
        if (role == null)
            throw new ApplicationException("role is not provided");

        var deletedRole = await roleRepository.Delete(role.Id, cancellationToken);
        if (deletedRole is null) throw new ApplicationException("Role not deleted.");
    }
}
