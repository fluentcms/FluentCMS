namespace FluentCMS.Services;

public interface IRoleService : IAutoRegisterService
{
    Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Role>> GetByIds(IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}

public class RoleService(IRoleRepository roleRepository) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetAll(cancellationToken);
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        return await roleRepository.Create(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToCreate);
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        _ = await roleRepository.GetById(role.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        return await roleRepository.Update(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToUpdate);
    }

    public async Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default)
    {
        _ = await roleRepository.GetById(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        return await roleRepository.Delete(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToDelete);
    }

    public Task<IEnumerable<Role>> GetByIds(IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        return roleRepository.GetByIds(roleIds, cancellationToken);
    }
}
