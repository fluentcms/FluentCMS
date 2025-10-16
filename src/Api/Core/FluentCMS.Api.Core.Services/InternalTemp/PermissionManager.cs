//namespace FluentCMS.Api.Core.Services.InternalTemp;

//public class PermissionManager(IApplicationExecutionContext executionContext, IPermissionRepository permissionRepository, IUserRoleRepository userRoleRepository, IGlobalSettingsRepository globalSettingsRepository, ISetupRepository setupRepository, IRoleRepository roleRepository) : IPermissionManager
//{
//    private bool _isSuperAdmin = false;
//    private List<UserRole> _userRoles;
//    private List<Role> _roles;
//    private List<Permission> _permissions;

//    public async Task Initialize(CancellationToken cancellationToken = default)
//    {
//        // setup is not done yet
//        if (!await setupRepository.IsInitialized(cancellationToken))
//        {
//            _isSuperAdmin = true;
//        }
//        else
//        {
//            // check for current user if is super admin
//            var globalSettings = await globalSettingsRepository.Get(cancellationToken);
//            if (globalSettings != null && globalSettings.SuperAdmins.Contains(executionContext.Username))
//                _isSuperAdmin = true;
//        }
//    }

//    public Task<bool> HasAccess(GlobalPermissionAction action, CancellationToken cancellationToken = default)
//    {
//        return Task.FromResult(_isSuperAdmin);
//    }

//    public async Task<bool> HasAccess(Guid siteId, SitePermissionAction action, CancellationToken cancellationToken = default)
//    {
//        // check for super admin
//        if (_isSuperAdmin)
//            return true;

//        if (executionContext.UserId is null || executionContext.UserId == Guid.Empty)
//            return false;

//        _userRoles ??= [.. await userRoleRepository.GetUserRoles(executionContext.UserId.Value, siteId, cancellationToken)];
//        _roles ??= [.. await roleRepository.GetAllForSite(siteId, cancellationToken)];

//        var adminRoleIds = _roles.Where(r => r.Type == RoleTypes.Administrators).Select(r => r.Id);
//        if (_userRoles.Any(ur => adminRoleIds.Contains(ur.RoleId)))
//            return true;

//        _permissions ??= [.. await permissionRepository.GetAllForSite(siteId, cancellationToken)];

//        // check if the user has access to the site
//        switch (action)
//        {
//            case SitePermissionAction.SiteContributor:
//                var siteContributorRoleIds = _permissions.Where(p => p.EntityId == siteId && (p.Action == Enum.GetName(SitePermissionAction.SiteContributor) || p.Action == Enum.GetName(SitePermissionAction.SiteAdmin))).Select(p => p.RoleId);
//                return _userRoles.Any(ur => siteContributorRoleIds.Contains(ur.RoleId));

//            case SitePermissionAction.SiteAdmin:
//                var siteAdminRoleIds = _permissions.Where(p => p.EntityId == siteId && p.Action == Enum.GetName(SitePermissionAction.SiteAdmin)).Select(p => p.RoleId);
//                return _userRoles.Any(ur => siteAdminRoleIds.Contains(ur.RoleId));

//            default:
//                break;
//        }

//        return false;
//    }

//    public async Task<IEnumerable<Site>> GetAccessible(IEnumerable<Site> sites, SitePermissionAction action, CancellationToken cancellationToken = default)
//    {
//        // Create a list to store sites where the user has access
//        var accessibleSites = new List<Site>();

//        // Iterate through each site and check if the user has access
//        foreach (var site in sites)
//        {
//            // Check if the user has access to the site for the given action
//            if (await HasAccess(site.Id, action, cancellationToken))
//            {
//                accessibleSites.Add(site); // If access is granted, add the site to the accessible list
//            }
//        }

//        // Return the filtered list of accessible sites
//        return accessibleSites;
//    }
//}

//public static class PermissionManagerExtensions
//{
//    public static async Task CheckSuperAdminPermission(this IPermissionManager permissionManager, CancellationToken cancellationToken = default)
//    {
//        cancellationToken.ThrowIfCancellationRequested();
//        if (!await permissionManager.HasAccess(GlobalPermissionAction.SuperAdmin, cancellationToken))
//        {
//            throw new PermissionDeniedException();
//        }
//    }

//    public static async Task CheckSiteAdminPermission(this IPermissionManager permissionManager, Guid siteId, CancellationToken cancellationToken = default)
//    {
//        cancellationToken.ThrowIfCancellationRequested();
//        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteAdmin, cancellationToken))
//        {
//            throw new PermissionDeniedException();
//        }
//    }

//    public static async Task CheckSiteContributorPermission(this IPermissionManager permissionManager, Guid siteId, CancellationToken cancellationToken = default)
//    {
//        cancellationToken.ThrowIfCancellationRequested();
//        if (!await permissionManager.HasAccess(siteId, SitePermissionAction.SiteContributor, cancellationToken))
//        {
//            throw new PermissionDeniedException();
//        }
//    }
//}


//public class PermissionDeniedException : EnhancedException
//{
//    public PermissionDeniedException() : base(ExceptionCodes.PermissionDenied)
//    {
//    }
//}
