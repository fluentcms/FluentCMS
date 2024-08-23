using FluentCMS.Providers;

namespace FluentCMS.Services;

public interface IUserRoleService : IAutoRegisterService
{
    Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
    Task<bool> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}

public class UserRoleService : IUserRoleService
{
    private readonly IUserRoleRepository _userRoleRepository;

    public UserRoleService(IUserRoleRepository userRoleRepository, IMessageSubscriber<Role> roleMessageSubscriber)
    {
        _userRoleRepository = userRoleRepository;

        roleMessageSubscriber.Subscribe(OnRoleMessageReceived);
    }

    public async Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        var userRoles = await _userRoleRepository.GetUserRoles(userId, siteId, cancellationToken);
        return userRoles.Select(x => x.RoleId).ToList();
    }

    public async Task<bool> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        // delete all exist UserRoles. 
        var existUserRoles = await _userRoleRepository.GetUserRoles(userId, siteId, cancellationToken);
        await _userRoleRepository.DeleteMany(existUserRoles.Select(x => x.Id), cancellationToken);

        // add all new UserRoles
        var userRoles = roleIds.Select(x => new UserRole
        {
            SiteId = siteId,
            RoleId = x,
            UserId = userId,
        });
        await _userRoleRepository.CreateMany(userRoles, cancellationToken);

        return true;
    }

    private async Task OnRoleMessageReceived(string actionName, Role role)
    {
        switch (actionName)
        {
            case ActionNames.RoleDeleted: await DeleteRoleFromUsers(role); break;
        }
    }

    private async Task DeleteRoleFromUsers(Role role)
    {
        var userRoles = await _userRoleRepository.GetByRoleId(role.Id, default);
        await _userRoleRepository.DeleteMany(userRoles.Select(x => x.Id));
    }
}
