using FluentCMS.Providers.MessageBusProviders;
using System.Data;

namespace FluentCMS.Services;

public interface IUserRoleService : IAutoRegisterService
{
    Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default);
    Task<bool> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}

public class UserRoleService(IUserRoleRepository userRoleRepository, IRoleRepository roleRepository, IAuthContext authContext) : IUserRoleService, IMessageHandler<Role>, IMessageHandler<User>
{
    public async Task<IEnumerable<Guid>> GetUserRoleIds(Guid userId, Guid siteId, CancellationToken cancellationToken = default)
    {
        var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
        if (!authContext.IsAuthenticated)
            return allRoles.Where(x => x.Type == RoleTypes.Guest || x.Type == RoleTypes.AllUsers).Select(x => x.Id);

        var userRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken) ?? [];
        var defaultRoles = allRoles.Where(x => x.Type == RoleTypes.Authenticated || x.Type == RoleTypes.AllUsers) ?? [];

        return userRoles.Select(x => x.RoleId).Concat(defaultRoles.Select(x => x.Id)).ToList();
    }

    public async Task<bool> Update(Guid userId, Guid siteId, IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        // find and remove default roles, just accept user defined roles.
        var allRoles = await roleRepository.GetAllForSite(siteId, cancellationToken);
        var validRoleIds = roleIds.Intersect(allRoles.Where(x => x.Type == RoleTypes.UserDefined || x.Type == RoleTypes.Administrators).Select(x => x.Id));

        // delete all exist UserRoles. 
        var existUserRoles = await userRoleRepository.GetUserRoles(userId, siteId, cancellationToken);
        await userRoleRepository.DeleteMany(existUserRoles.Select(x => x.Id), cancellationToken);

        // add all new UserRoles
        var userRoles = validRoleIds.Select(x => new UserRole
        {
            SiteId = siteId,
            RoleId = x,
            UserId = userId,
        });
        await userRoleRepository.CreateMany(userRoles, cancellationToken);

        return true;
    }

    public async Task Handle(Message<Role> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.RoleDeleted:
                await DeleteRoleForUsers(notification.Payload.Id, cancellationToken);
                break;
        }
    }

    private async Task DeleteRoleForUsers(Guid roleId, CancellationToken cancellationToken)
    {
        var userRoles = await userRoleRepository.GetByRoleId(roleId, cancellationToken);
        await userRoleRepository.DeleteMany(userRoles.Select(x => x.Id));
    }

    public async Task Handle(Message<User> notification, CancellationToken cancellationToken)
    {
        switch (notification.Action)
        {
            case ActionNames.UserDeleted:
                await DeleteByUserId(notification.Payload.Id, cancellationToken);
                break;
        }
    }

    private async Task DeleteByUserId(Guid userId, CancellationToken cancellationToken)
    {
        var userRoles = await userRoleRepository.GetByUserId(userId, cancellationToken);
        await userRoleRepository.DeleteMany(userRoles.Select(x => x.Id), cancellationToken);
    }
}
