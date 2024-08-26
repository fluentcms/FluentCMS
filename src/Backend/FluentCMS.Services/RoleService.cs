﻿using FluentCMS.Providers;

namespace FluentCMS.Services;

public interface IRoleService : IAutoRegisterService
{
    Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default);
    Task<Role> Create(Role role, CancellationToken cancellationToken = default);
    Task<Role> Update(Role role, CancellationToken cancellationToken = default);
    Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default);
    Task<Role?> GetById(Guid roleId, CancellationToken cancellationToken = default);
}

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IMessagePublisher<Role> _roleMessagePublisher;

    public RoleService(IRoleRepository roleRepository, IMessageSubscriber<Site> siteMessageSubscriber, IMessagePublisher<Role> roleMessagePublisher)
    {
        _roleRepository = roleRepository;
        _roleMessagePublisher = roleMessagePublisher;

        siteMessageSubscriber.Subscribe(OnSiteMessageReceived);
    }

    public async Task<IEnumerable<Role>> GetAllForSite(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await _roleRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<IEnumerable<Role>> GetAll(CancellationToken cancellationToken = default)
    {
        return await _roleRepository.GetAll(cancellationToken);
    }

    public async Task<Role> Create(Role role, CancellationToken cancellationToken)
    {
        var sameRole = await _roleRepository.GetByNameAndSiteId(role.SiteId, role.Name, cancellationToken);

        if (sameRole != null)
            throw new AppException(ExceptionCodes.RoleNameIsDuplicated);

        var newRole = await _roleRepository.Create(role, cancellationToken) ??
           throw new AppException(ExceptionCodes.RoleUnableToCreate);

        await _roleMessagePublisher.Publish(ActionNames.RoleCreated, newRole);

        return newRole;
    }

    public async Task<Role> Update(Role role, CancellationToken cancellationToken)
    {
        _ = await _roleRepository.GetById(role.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        var sameRole = await _roleRepository.GetByNameAndSiteId(role.SiteId, role.Name, cancellationToken);

        if (sameRole != null && sameRole.Id != role.Id)
            throw new AppException(ExceptionCodes.RoleNameIsDuplicated);

        var updatedRole = await _roleRepository.Update(role, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToUpdate);

        await _roleMessagePublisher.Publish(ActionNames.RoleUpdated, updatedRole);

        return updatedRole;
    }

    public async Task<Role> Delete(Guid roleId, CancellationToken cancellationToken = default)
    {
        var existRole = await _roleRepository.GetById(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleNotFound);

        // check for system roles, they cant be deleted.
        // we need them for system purposes. 
        if (existRole.Type != RoleTypes.UserDefined)
            throw new AppException(ExceptionCodes.RoleCanNotBeDeleted);

        var deleteRole = await _roleRepository.Delete(roleId, cancellationToken) ??
            throw new AppException(ExceptionCodes.RoleUnableToDelete);

        await _roleMessagePublisher.Publish(ActionNames.RoleDeleted, deleteRole);

        return deleteRole;
    }

    public Task<Role?> GetById(Guid roleId, CancellationToken cancellationToken = default)
    {
        return _roleRepository.GetById(roleId, cancellationToken);
    }

    private async Task OnSiteMessageReceived(string actionName, Site site)
    {
        switch (actionName)
        {
            case ActionNames.SiteCreated: await AddPrimaryRolesForSite(site); break;
            case ActionNames.SiteDeleted: await DeleteAllRolesOfSite(site); break;
        }
    }

    private async Task DeleteAllRolesOfSite(Site site)
    {
        var siteRoles = await GetAllForSite(site.Id, default);
        await _roleRepository.DeleteMany(siteRoles.Select(x => x.Id));
    }

    private async Task AddPrimaryRolesForSite(Site site)
    {
        var primaryRoles = new List<Role>() {
            new() {
                Name="Administrators",
                Description = "Default administrators role with full access to the site",
                Type=RoleTypes.Administrators,
                SiteId=site.Id,
            },
            new() {
                Name="Authenticated Users",
                Description = "All authenticated users (logged in users)",
                Type=RoleTypes.Authenticated,
                SiteId=site.Id,
            },
            new() {
                Name="Guests",
                Description = "Un-authenticated users (not logged in users)",
                Type=RoleTypes.Guest,
                SiteId=site.Id,
            },
            new() {
                Name="All Users",
                Description = "All users (authenticated or un-authenticated users)",
                Type=RoleTypes.AllUsers,
                SiteId=site.Id,
            }
         };

        await _roleRepository.CreateMany(primaryRoles, default);
    }
}
