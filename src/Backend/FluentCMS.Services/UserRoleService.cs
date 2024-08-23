namespace FluentCMS.Services;

public interface IUserRoleService : IAutoRegisterService
{
    Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
    Task<bool> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}

public class UserRoleService(IUserRoleRepository userRoleRepository) : IUserRoleService
{
    public async Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        return await userRoleRepository.GetUserRoleIds(userId, siteId, cancellationToken);
    }

    public async Task<bool> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        // delete all exist UserRoles. 
        var existUserRoles = await userRoleRepository.GetUserRoleIds(userId, siteId, cancellationToken);
        await userRoleRepository.DeleteMany(existUserRoles, cancellationToken);

        // add all new UserRoles
        var userRoles = roleIds.Select(x => new UserRole
        {
            SiteId = siteId,
            RoleId = x,
            UserId = userId,
        });
        await userRoleRepository.CreateMany(userRoles, cancellationToken);

        return true;
    }
}
