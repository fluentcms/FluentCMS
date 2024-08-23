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
        var userRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken);
        return userRoles.Select(x => x.RoleId).ToList();
    }

    public async Task<bool> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        // delete all exist UserRoles. 
        var existUserRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken);
        await userRoleRepository.DeleteMany(existUserRoles.Select(x => x.Id), cancellationToken);

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
