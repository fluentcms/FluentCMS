namespace FluentCMS.Services.Permissions;

public class PermissionManager : IPermissionManager
{
    private readonly IApiExecutionContext _apiExecutionContext;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IGlobalSettingsRepository _globalSettingsRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly bool _isSuperAdmin = false;
    private List<UserRole> _userRoles = [];
    private List<Role> _roles = [];
    private List<Permission> _permissions = [];

    public PermissionManager(IApiExecutionContext apiExecutionContext, IPermissionRepository permissionRepository, IUserRoleRepository userRoleRepository, IGlobalSettingsRepository globalSettingsRepository, IRoleRepository roleRepository)
    {
        _apiExecutionContext = apiExecutionContext;
        _permissionRepository = permissionRepository;
        _userRoleRepository = userRoleRepository;
        _globalSettingsRepository = globalSettingsRepository;
        _roleRepository = roleRepository;

        // setup is not done yet
        if (!_globalSettingsRepository.Initialized().GetAwaiter().GetResult())
        {
            _isSuperAdmin = true;
        }
        else
        {
            // check for current user if is super admin
            var globalSettings = _globalSettingsRepository.Get().GetAwaiter().GetResult();
            if (globalSettings != null && globalSettings.SuperAdmins.Contains(_apiExecutionContext.Username))
                _isSuperAdmin = true;
        }
    }

    public Task<bool> HasAccess(GlobalPermissionAction action, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isSuperAdmin);
    }

    public async Task<bool> HasAccess(Guid siteId, SitePermissionAction action, CancellationToken cancellationToken = default)
    {
        // check for super admin
        if (_isSuperAdmin)
            return true;

        _userRoles ??= (await _userRoleRepository.GetUserRoles(_apiExecutionContext.UserId, siteId, cancellationToken)).Where(ur => ur.SiteId == siteId).ToList();
        _roles ??= (await _roleRepository.GetAllForSite(siteId, cancellationToken)).ToList();

        var adminRoleIds = _roles.Where(r => r.Type == RoleTypes.Administrators).Select(r => r.Id);
        if (_userRoles.Any(ur => adminRoleIds.Contains(ur.RoleId)))
            return true;

        _permissions ??= (await _permissionRepository.GetAllForSite(siteId, cancellationToken)).ToList();

        // check if the user has access to the site
        switch (action)
        {
            case SitePermissionAction.SiteContributor:
                var siteContributorRoleIds = _permissions.Where(p => p.EntityId == siteId && (p.Action == Enum.GetName(SitePermissionAction.SiteContributor) || p.Action == Enum.GetName(SitePermissionAction.SiteAdmin))).Select(p => p.RoleId);
                return _userRoles.Any(ur => siteContributorRoleIds.Contains(ur.RoleId));

            case SitePermissionAction.SiteAdmin:
                var siteAdminRoleIds = _permissions.Where(p => p.EntityId == siteId && (p.Action == Enum.GetName(SitePermissionAction.SiteAdmin))).Select(p => p.RoleId);
                return _userRoles.Any(ur => siteAdminRoleIds.Contains(ur.RoleId));

            default:
                break;
        }

        return false;
    }

    public async Task<IEnumerable<Site>> GetAccessible(IEnumerable<Site> sites, SitePermissionAction action, CancellationToken cancellationToken = default)
    {
        // Create a list to store sites where the user has access
        var accessibleSites = new List<Site>();

        // Iterate through each site and check if the user has access
        foreach (var site in sites)
        {
            // Check if the user has access to the site for the given action
            if (await HasAccess(site.Id, action, cancellationToken))
            {
                accessibleSites.Add(site); // If access is granted, add the site to the accessible list
            }
        }

        // Return the filtered list of accessible sites
        return accessibleSites;
    }
}
