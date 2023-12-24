namespace FluentCMS.Services;

public interface IRoleService : IService
{
    Task<IEnumerable<Role>> GetAll(Guid appId, CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Delete(Guid appId, Guid roleId, CancellationToken cancellationToken = default);
}

public class RoleService(IRoleRepository roleRepository, IAppRepository appRepository) : IRoleService
{
    public async Task<IEnumerable<Role>> GetAll(Guid appId, CancellationToken cancellationToken = default)
    {
        return await roleRepository.GetAll(appId, cancellationToken);
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

    public async Task<Role> Delete(Guid appId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetById(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        if (role.AppId != appId)
            throw new AppException(ExceptionCodes.RoleInvalidAppId);

        return await roleRepository.Delete(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToDelete);
    }

    private async Task<App> GetApp(string appSlug, CancellationToken cancellationToken = default)
    {
        return await appRepository.GetBySlug(appSlug, cancellationToken) ??
            throw new AppException(ExceptionCodes.AppNotFound);
    }
}
