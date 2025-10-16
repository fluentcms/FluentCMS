namespace FluentCMS.Api.Plugins.IdentityManagement.Services;

internal class SiteScopedRoleValidator(ISiteRepository siteRepository, IRoleRepository roleRepository, ILogger<SiteScopedRoleValidator> logger, IdentityErrorDescriber? errors = null) : IRoleValidator<Role>
{
    private readonly IdentityErrorDescriber _describer = errors ?? new IdentityErrorDescriber();

    private async Task<List<IdentityError>?> SoftValidate(RoleManager<Role> manager, Role role)
    {
        List<IdentityError>? errors = null;
        var roleName = await manager.GetRoleNameAsync(role).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(roleName))
        {
            errors ??= [];
            errors.Add(_describer.InvalidRoleName(roleName));
            return errors;
        }

        if (role.SiteId == Guid.Empty)
        {
            errors ??= [];
            errors.Add(new IdentityError
            {
                Code = "InvalidSiteId",
                Description = "Site ID cannot be empty."
            });
            return errors;
        }

        return errors;
    }

    public async Task<IdentityResult> ValidateAsync(RoleManager<Role> manager, Role role)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(role);

        // 1. Basic validations
        var errors = await SoftValidate(manager, role).ConfigureAwait(false);
        if (errors?.Count > 0)
        {
            return IdentityResult.Failed([.. errors]);
        }

        // 2. Context retrieval
        var (siteExists, site) = await siteRepository.TryGet(role.SiteId);
        if (!siteExists)
        {
            errors ??= [];
            errors.Add(new IdentityError
            {
                Code = "SiteNotFound",
                Description = $"Site with ID {role.SiteId} does not exist."
            });
            return IdentityResult.Failed([.. errors]);
        }

        // 3. Role name duplication check within the same site
        var normalizedName = manager.NormalizeKey(role.Name);
        var allRolesForSite = await roleRepository.GetAllForSite(role.SiteId);
        var duplicateRole = allRolesForSite.Where(r => r.SiteId == role.SiteId && r.NormalizedName == normalizedName && r.Id != role.Id).FirstOrDefault();

        if (duplicateRole != null)
        {
            logger.LogWarning("Duplicate role name '{RoleName}' attempted for site {SiteId}", role.Name, role.SiteId);
            errors ??= [];
            errors.Add(_describer.DuplicateRoleName(role.Name!));
            return IdentityResult.Failed([.. errors]);
        }

        return IdentityResult.Success;
    }
}
