namespace FluentCMS.Services;

public interface IRoleService : IAutoRegisterService
{
    Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role?> GetById(Guid roleId, CancellationToken cancellationToken = default);
}

public class RoleService(IRoleRepository roleRepository) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetAll(cancellationToken);
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        // check if role name already exists
        var roles = await roleRepository.GetAll(cancellationToken);
        if (roles.Any(r => r.Name.Equals(role.Name, StringComparison.CurrentCultureIgnoreCase)))
            throw new AppException(ExceptionCodes.RoleNameMustBeUnique);

        return await roleRepository.Create(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToCreate);
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        // check if role exists
        var existing = await roleRepository.GetById(role.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        // only default roles are updatable
        if (existing.Type != RoleType.Default)
            throw new AppException(ExceptionCodes.RoleUnableToUpdate);

        // force role type to default
        role.Type = RoleType.Default;

        // check if role name already exists
        var roles = await roleRepository.GetAll(cancellationToken);
        if (roles.Any(r => r.Id != role.Id && r.Name.Equals(role.Name, StringComparison.CurrentCultureIgnoreCase)))
            throw new AppException(ExceptionCodes.RoleNameMustBeUnique);

        return await roleRepository.Update(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToUpdate);
    }

    public async Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default)
    {
        // check if role exists
        _ = await roleRepository.GetById(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        return await roleRepository.Delete(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToDelete);
    }

    public Task<Role?> GetById(Guid roleId, CancellationToken cancellationToken = default)
    {
        return roleRepository.GetById(roleId, cancellationToken);
    }
}
