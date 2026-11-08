using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Authorization;

internal sealed record PermissionGrant(string Permission, PermissionConstraints Constraints);

internal sealed record RoleInfo(Guid Id, Guid? SiteId, string Name, string? Description, bool BuiltIn, bool RequireTwoFactor, IReadOnlyList<PermissionGrant> Grants);

// A role as it is to be. The site is the new role's; an update keeps the site the role has, and the two-factor flag when
// the request leaves it out.
internal sealed record RoleRequest(string Name, string? Description, IReadOnlyList<PermissionGrant> Grants)
{
    public Guid? SiteId { get; init; }

    public bool? RequireTwoFactor { get; init; }
}

// Roles and their grants for the Management API. Writing a role needs roles.manage where the role applies, and a role holds
// only what the caller holds itself: no grant above one's own reach, neither added nor taken from a role the caller could not
// have made. Users, invitations and keys ask here whether the caller may hand a role out. A role is written through
// CmsDbContext, whose interceptor raises its permissions version with the change, and deleted only while no user holds it
// and no key that is not revoked uses it, so the cascade of the database drops no holder without its event. An edit that
// takes `*` from an instance-wide role keeps the last Super Admin.
internal sealed class RoleService(
    CmsDbContext context,
    PermissionCatalogue catalogue,
    RowRuleRegistry rules,
    ManagementAuthority authority,
    SuperAdminGuard superAdmins,
    IActivityLog activity,
    IOperationContextAccessor accessor,
    EffectiveGrantCache cache,
    TimeProvider time)
{
    private const int NameLength = 128;
    private const int DescriptionLength = 512;
    private const int MaxResources = 100;
    private const int ResourceLength = 128;

    private static readonly PrincipalBinding Unbound = new(null, null);

    private static readonly string[] Readers = [CorePermissions.RolesManage, CorePermissions.UsersManage, CorePermissions.ApiKeysManage];

    public async Task<IReadOnlyList<PermissionEntry>> CatalogueAsync(CancellationToken cancellationToken)
    {
        if (!await AnyAsync(Readers, authority.CurrentSite?.Value, cancellationToken))
        {
            throw new ManagementAccessDeniedException($"The permission catalogue needs {CorePermissions.RolesManage}, {CorePermissions.UsersManage} or {CorePermissions.ApiKeysManage}.");
        }

        return catalogue.Entries;
    }

    // The roles the caller manages or hands out: those of its site and the instance-wide ones, which every site uses.
    public async Task<IReadOnlyList<RoleInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await context.Set<Role>().AsNoTracking().ToListAsync(cancellationToken);
        var grants = await GrantsAsync([.. roles.Select(role => role.Id)], cancellationToken);

        List<RoleInfo> visible = [];
        foreach (var role in roles.OrderBy(role => role.SiteId is not null).ThenBy(role => role.SiteId).ThenBy(role => role.Name, StringComparer.Ordinal))
        {
            if (await CanReadAsync(role, cancellationToken))
            {
                visible.Add(Info(role, grants[role.Id]));
            }
        }

        if (visible.Count == 0 && !await AnyAsync(Readers, authority.CurrentSite?.Value, cancellationToken))
        {
            throw new ManagementAccessDeniedException($"Roles are read with {CorePermissions.RolesManage}, {CorePermissions.UsersManage} or {CorePermissions.ApiKeysManage}.");
        }

        return visible;
    }

    public async Task<RoleInfo> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await context.Set<Role>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (role is null || !await CanReadAsync(role, cancellationToken))
        {
            throw NotFound(id);
        }

        return Info(role, (await GrantsAsync([id], cancellationToken))[id]);
    }

    public async Task<RoleInfo> CreateAsync(RoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await authority.RequireAsync(CorePermissions.RolesManage, request.SiteId, cancellationToken);
        if (request.SiteId is { } site && !await context.Set<Site>().AnyAsync(item => item.Id == site, cancellationToken))
        {
            throw new ValidationException([new ValidationError("siteId", SiteErrorCodes.SiteNotFound, $"There is no site {site:D}.")]);
        }

        var (name, description, grants) = await ValidateAsync(request, request.SiteId, id: null, cancellationToken);
        await RequireCoveredAsync(grants, request.SiteId, name, cancellationToken);

        var role = new Role { Id = Guid.CreateVersion7(), SiteId = request.SiteId, Name = name, Description = description, RequireTwoFactor = request.RequireTwoFactor ?? false };
        context.Add(role);
        context.AddRange(Rows(role.Id, grants));
        await SaveAsync(role, cancellationToken);

        await LogAsync("role.created", role, null, cancellationToken);
        return Info(role, grants);
    }

    // Replaces the name, the description and every grant. A built-in role keeps its name, and Super Admin keeps everything
    // but whether it needs a second factor.
    public async Task<RoleInfo> UpdateAsync(Guid id, RoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await context.Set<Role>().FirstOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw NotFound(id);
        await authority.RequireAsync(CorePermissions.RolesManage, role.SiteId, cancellationToken);

        var (name, description, grants) = await ValidateAsync(request, role.SiteId, id, cancellationToken);
        if (role.BuiltIn && name != role.Name)
        {
            throw new ValidationException([new ValidationError("name", RoleErrorCodes.InvalidRole, "A built-in role keeps its name.")]);
        }

        var stored = await context.Set<RoleGrant>().Where(grant => grant.RoleId == id).ToListAsync(cancellationToken);
        if (role.Id == BuiltInRoles.SuperAdmin && (description != role.Description || !SameGrants([.. stored.Select(Grant)], grants)))
        {
            throw Fixed("Super Admin holds every permission; of it, only requireTwoFactor can be changed.");
        }

        await RequireCoveredAsync([.. stored.Select(Grant)], role.SiteId, role.Name, cancellationToken);
        await RequireCoveredAsync(grants, role.SiteId, name, cancellationToken);

        role.Name = name;
        role.Description = description;
        role.RequireTwoFactor = request.RequireTwoFactor ?? role.RequireTwoFactor;
        if (role.Id != BuiltInRoles.SuperAdmin)
        {
            context.RemoveRange(stored);
            context.AddRange(Rows(role.Id, grants));
        }

        if (role.SiteId is null && stored.Select(Grant).Any(SuperAdminGuard.IsSuperGrant) && !grants.Any(SuperAdminGuard.IsSuperGrant))
        {
            await superAdmins.ChangeRoleAsync(context, role, () => SaveAsync(role, cancellationToken), cancellationToken);
        }
        else
        {
            await SaveAsync(role, cancellationToken);
        }

        await LogAsync("role.updated", role, Changes([.. stored.Select(Grant)], grants), cancellationToken);
        return Info(role, grants);
    }

    // One statement deletes the role only while nothing holds it, so a holder added after the check stops it too. A revoked
    // key keeps no role that matters, and goes with the role.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await context.Set<Role>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw NotFound(id);
        await authority.RequireAsync(CorePermissions.RolesManage, role.SiteId, cancellationToken);
        if (role.BuiltIn)
        {
            throw Fixed("A built-in role cannot be deleted.");
        }

        await RequireCoveredAsync([.. (await GrantsAsync([id], cancellationToken))[id]], role.SiteId, role.Name, cancellationToken);

        var deleted = await context.Set<Role>()
            .Where(item => item.Id == id
                && !context.Set<CmsUserRole>().Any(membership => membership.RoleId == id)
                && !context.Set<ApiKey>().Any(key => key.RoleId == id && key.RevokedAt == null))
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            if (!await context.Set<Role>().AnyAsync(item => item.Id == id, cancellationToken))
            {
                throw NotFound(id);
            }

            var users = await context.Set<CmsUserRole>().CountAsync(membership => membership.RoleId == id, cancellationToken);
            var keys = await context.Set<ApiKey>().CountAsync(key => key.RoleId == id && key.RevokedAt == null, cancellationToken);
            throw new ValidationException([new ValidationError("id", RoleErrorCodes.RoleInUse,
                $"The role is held by {users} user(s) and {keys} API key(s) that are not revoked. Take it from the users and revoke the keys first.")]);
        }

        cache.Remove(id);
        await LogAsync("role.deleted", role, null, cancellationToken);
    }

    // For a user or an invitation: the caller must hold every grant of each role. A role that does not exist is the caller's
    // own error to report.
    public async Task RequireAssignableAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        List<Guid> ids = [.. roleIds.Distinct()];
        var roles = await context.Set<Role>().AsNoTracking().Where(role => ids.Contains(role.Id)).ToListAsync(cancellationToken);
        var grants = await GrantsAsync(ids, cancellationToken);
        foreach (var role in roles)
        {
            await RequireCoveredAsync([.. grants[role.Id]], role.SiteId, role.Name, cancellationToken);
        }
    }

    // For a key: the role as the key's binding narrows it, so a key bound to one site needs the role's grants in that site only.
    public async Task RequireKeyRoleAsync(Guid roleId, IReadOnlyCollection<Guid>? sites, IReadOnlyCollection<string>? types, CancellationToken cancellationToken)
    {
        if (await context.Set<Role>().AsNoTracking().FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken) is not { } role)
        {
            return;
        }

        var binding = new PrincipalBinding(sites?.ToHashSet(), types?.ToHashSet(StringComparer.Ordinal));
        var grants = (await GrantsAsync([roleId], cancellationToken))[roleId].Select(grant => (grant.Permission, grant.Constraints));
        if (!await authority.CoversAsync(grants, role.SiteId, binding, cancellationToken))
        {
            throw Beyond(role.Name);
        }
    }

    private async Task<bool> CanReadAsync(Role role, CancellationToken cancellationToken) =>
        await AnyAsync(Readers, role.SiteId ?? authority.CurrentSite?.Value, cancellationToken);

    private async Task<bool> AnyAsync(string[] permissions, Guid? site, CancellationToken cancellationToken)
    {
        foreach (var permission in permissions)
        {
            if (await authority.AllowsAsync(permission, site, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task RequireCoveredAsync(IReadOnlyList<PermissionGrant> grants, Guid? site, string name, CancellationToken cancellationToken)
    {
        if (!await authority.CoversAsync(grants.Select(grant => (grant.Permission, grant.Constraints)), site, Unbound, cancellationToken))
        {
            throw Beyond(name);
        }
    }

    private async Task<ILookup<Guid, PermissionGrant>> GrantsAsync(List<Guid> roleIds, CancellationToken cancellationToken)
    {
        var rows = await context.Set<RoleGrant>().AsNoTracking().Where(grant => roleIds.Contains(grant.RoleId)).ToListAsync(cancellationToken);
        return rows.OrderBy(grant => grant.Id).ToLookup(grant => grant.RoleId, Grant);
    }

    private async Task<(string Name, string? Description, List<PermissionGrant> Grants)> ValidateAsync(RoleRequest request, Guid? site, Guid? id, CancellationToken cancellationToken)
    {
        List<ValidationError> errors = [];
        var name = (request.Name ?? "").Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        if (name.Length == 0 || name.Length > NameLength)
        {
            errors.Add(new ValidationError("name", RoleErrorCodes.InvalidRole, $"A role has a name of 1 to {NameLength} characters."));
        }
        else if (await context.Set<Role>().AnyAsync(role => role.SiteId == site && role.Name == name && role.Id != id, cancellationToken))
        {
            errors.Add(new ValidationError("name", RoleErrorCodes.InvalidRole, $"A role named '{name}' exists where this one applies."));
        }

        if (description is { Length: > DescriptionLength })
        {
            errors.Add(new ValidationError("description", RoleErrorCodes.InvalidRole, $"A description has at most {DescriptionLength} characters."));
        }

        var sites = await context.Set<Site>().AsNoTracking().Select(item => item.Id).ToListAsync(cancellationToken);
        List<PermissionGrant> grants = [];
        for (var index = 0; index < request.Grants.Count; index++)
        {
            grants.Add(ValidateGrant(request.Grants[index], $"grants[{index}]", site, sites, errors));
        }

        return errors.Count > 0 ? throw new ValidationException(errors) : (name, description, grants);
    }

    private PermissionGrant ValidateGrant(PermissionGrant grant, string path, Guid? site, List<Guid> sites, List<ValidationError> errors)
    {
        if (!catalogue.IsGrantable(grant.Permission))
        {
            errors.Add(Invalid(path + ".permission", $"'{grant.Permission}' names no permission of the catalogue."));
        }

        var constraints = grant.Constraints;
        if (constraints.Sites is { } limited)
        {
            if (limited.Count == 0)
            {
                errors.Add(Invalid(path + ".constraints.sites", "A grant is limited to at least one site, or to none by leaving sites out."));
            }
            else if (site is { } own && limited.Any(item => item.Value != own))
            {
                errors.Add(Invalid(path + ".constraints.sites", "A role of a site grants only in that site."));
            }
            else if (limited.Any(item => !sites.Contains(item.Value)))
            {
                errors.Add(Invalid(path + ".constraints.sites", "A grant is limited only to sites that exist."));
            }
        }

        List<string>? locales = null;
        if (constraints.Locales is { } codes)
        {
            locales = [];
            foreach (var code in codes)
            {
                if (LocaleTag.TryNormalize(code, out var normalized))
                {
                    locales.Add(normalized);
                }
                else
                {
                    errors.Add(new ValidationError(path + ".constraints.locales", LocaleErrorCodes.InvalidLocale, $"'{code}' is not a well-formed BCP 47 language tag."));
                }
            }

            if (codes.Count == 0)
            {
                errors.Add(Invalid(path + ".constraints.locales", "A grant is limited to at least one locale, or to none by leaving locales out."));
            }
        }

        if (constraints.Fields is { } fields && (fields.Count == 0 || fields.Any(string.IsNullOrWhiteSpace)))
        {
            errors.Add(Invalid(path + ".constraints.fields", "A grant is limited to named fields, at least one, or to none by leaving fields out."));
        }

        if (constraints.RowRule is { } rule && !rules.Has(rule))
        {
            errors.Add(Invalid(path + ".constraints.rowRule", $"There is no row rule '{rule}'."));
        }

        if (constraints.Resources is { } resources && (resources.Count is 0 or > MaxResources || resources.Any(resource => string.IsNullOrWhiteSpace(resource) || resource.Length > ResourceLength)))
        {
            errors.Add(Invalid(path + ".constraints.resources", $"A grant is limited to from 1 to {MaxResources} resources, each an id of at most {ResourceLength} characters, or to none by leaving resources out."));
        }

        return new PermissionGrant(grant.Permission, new PermissionConstraints
        {
            Sites = constraints.Sites,
            Locales = locales?.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Fields = constraints.Fields,
            RowRule = constraints.RowRule,
            Resources = constraints.Resources?.Distinct(StringComparer.Ordinal).ToList(),
        });
    }

    private async Task SaveAsync(Role role, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            throw new ValidationException([new ValidationError("id", RoleErrorCodes.RoleChanged, "The role changed while it was being saved; read it again and retry.")]);
        }
        catch (DbUpdateException)
        {
            if (await NameTakenAsync(role, cancellationToken))
            {
                throw new ValidationException([new ValidationError("name", RoleErrorCodes.InvalidRole, $"A role named '{role.Name}' exists where this one applies.")]);
            }

            throw;
        }
    }

    private async Task<bool> NameTakenAsync(Role role, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        return await context.Set<Role>().AnyAsync(item => item.SiteId == role.SiteId && item.Name == role.Name && item.Id != role.Id, cancellationToken);
    }

    // A role of a site is that site's business; an instance-wide one belongs to no site.
    private async Task LogAsync(string action, Role role, IReadOnlyDictionary<string, string?>? details, CancellationToken cancellationToken)
    {
        var source = accessor.Current is { } operation ? EventSource.From(operation) : EventSource.FromSystem(time.GetUtcNow());
        var entry = new ActivityEntry(action) { TargetType = "role", TargetId = role.Id.ToString("D"), TargetLabel = role.Name, Details = details };
        await activity.WriteAsync(entry, source with { SiteId = role.SiteId ?? Guid.Empty }, cancellationToken);
    }

    // The grants an update added and took away, each as its permission and its constraints, if it has any.
    private static Dictionary<string, string?> Changes(List<PermissionGrant> before, IReadOnlyList<PermissionGrant> after)
    {
        var old = before.Select(Key).ToHashSet();
        var kept = after.Select(Key).ToHashSet();
        return new Dictionary<string, string?>
        {
            ["added"] = Text(kept.Where(grant => !old.Contains(grant))),
            ["removed"] = Text(old.Where(grant => !kept.Contains(grant))),
        };

        static string Text(IEnumerable<(string Permission, string? Constraints)> grants) =>
            string.Join("; ", grants.Order().Select(grant => grant.Constraints is null ? grant.Permission : $"{grant.Permission} {grant.Constraints}"));
    }

    private static IEnumerable<RoleGrant> Rows(Guid roleId, IEnumerable<PermissionGrant> grants) =>
        grants.Select(grant => new RoleGrant { Id = Guid.CreateVersion7(), RoleId = roleId, Permission = grant.Permission, Constraints = grant.Constraints.ToJson() });

    private static PermissionGrant Grant(RoleGrant grant) => new(grant.Permission, PermissionConstraints.FromJson(grant.Constraints));

    private static bool SameGrants(List<PermissionGrant> left, List<PermissionGrant> right) =>
        left.Select(Key).Order().SequenceEqual(right.Select(Key).Order());

    private static (string, string?) Key(PermissionGrant grant) => (grant.Permission, grant.Constraints.ToJson());

    private static RoleInfo Info(Role role, IEnumerable<PermissionGrant> grants) =>
        new(role.Id, role.SiteId, role.Name, role.Description, role.BuiltIn, role.RequireTwoFactor, [.. grants]);

    private static ValidationError Invalid(string path, string message) => new(path, RoleErrorCodes.InvalidGrant, message);

    private static ValidationException NotFound(Guid id) =>
        new([new ValidationError("id", RoleErrorCodes.RoleNotFound, $"There is no role {id:D}.")]);

    private static ValidationException Fixed(string message) => new([new ValidationError("id", RoleErrorCodes.RoleFixed, message)]);

    private static ManagementAccessDeniedException Beyond(string role) =>
        new($"The role '{role}' grants more than the caller holds, so the caller may not make, change or hand it out.");
}
