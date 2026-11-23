using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Transfer;
using FluentCMS.Events;
using FluentCMS.Transfer;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Authorization;

internal sealed record PermissionGrant(string Permission, PermissionConstraints Constraints);

internal sealed record RoleInfo(Guid Id, Guid? SiteId, string Name, string? Description, bool BuiltIn, bool RequireTwoFactor, IReadOnlyList<PermissionGrant> Grants);

// A permission that a provider names for a built-in role by default: the provider, the plugin that added it, if any, and
// every permission of that plugin in the catalogue (of the provider alone for one the host registered itself).
internal sealed record DefaultGrant(Guid RoleId, string Permission, Type Provider, string? Plugin, IReadOnlyCollection<string> PluginPermissions);

// What a start gave, by the name of the role, and the defaults it refused because the catalogue does not hold them as
// permissions of the provider that names them.
internal sealed record DefaultGrantOutcome(IReadOnlyList<(string Role, IReadOnlyList<string> Permissions)> Given, IReadOnlyList<DefaultGrant> Refused);

// A role as it is to be. The site is the new role's; an update keeps the site the role has, and the two-factor flag when
// the request leaves it out.
internal sealed record RoleRequest(string Name, string? Description, IReadOnlyList<PermissionGrant> Grants)
{
    public Guid? SiteId { get; init; }

    public bool? RequireTwoFactor { get; init; }
}

// Roles and their grants for the Management API. Writing a role needs roles.manage where the role applies, and a role holds
// only what the caller holds itself: no grant above one's own reach, neither added nor taken from a role the caller could not
// have made. Users, invitations, keys and member groups ask here whether the caller may hand a role out. A role is written
// through CmsDbContext, whose interceptor raises its permissions version with the change, and deleted only while no user
// holds it, no key that is not revoked uses it and no member group is bound to it, so the cascade of the database drops
// no holder without its event. An edit that takes `*` from an instance-wide role keeps the last Super Admin.
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

    // Each attempt reads what another start saved in between, so only roles that keep changing use them all.
    private const int DefaultGrantAttempts = 5;

    private static readonly PrincipalBinding Unbound = new(null, null);

    private static readonly string[] Readers = [CorePermissions.RolesManage, CorePermissions.UsersManage, CorePermissions.ApiKeysManage, CorePermissions.MembersManage];

    public async Task<IReadOnlyList<PermissionEntry>> CatalogueAsync(CancellationToken cancellationToken)
    {
        if (!await AnyAsync(Readers, authority.CurrentSite?.Value, cancellationToken))
        {
            throw new ManagementAccessDeniedException($"The permission catalogue needs {string.Join(", ", Readers[..^1])} or {Readers[^1]}.");
        }

        return catalogue.Entries;
    }

    // What a grant's rowRule may name, for whoever may read the catalogue.
    public async Task<IReadOnlyList<RowRuleEntry>> RowRulesAsync(CancellationToken cancellationToken)
    {
        if (!await AnyAsync(Readers, authority.CurrentSite?.Value, cancellationToken))
        {
            throw new ManagementAccessDeniedException($"The row rules need {string.Join(", ", Readers[..^1])} or {Readers[^1]}.");
        }

        return rules.Entries;
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
            throw new ManagementAccessDeniedException($"Roles are read with {string.Join(", ", Readers[..^1])} or {Readers[^1]}.");
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
                && !context.Set<ApiKey>().Any(key => key.RoleId == id && key.RevokedAt == null)
                && !context.Set<MemberGroup>().Any(group => group.RoleId == id))
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            if (!await context.Set<Role>().AnyAsync(item => item.Id == id, cancellationToken))
            {
                throw NotFound(id);
            }

            var users = await context.Set<CmsUserRole>().CountAsync(membership => membership.RoleId == id, cancellationToken);
            var keys = await context.Set<ApiKey>().CountAsync(key => key.RoleId == id && key.RevokedAt == null, cancellationToken);
            var groups = await context.Set<MemberGroup>().CountAsync(group => group.RoleId == id, cancellationToken);
            throw new ValidationException([new ValidationError("id", RoleErrorCodes.RoleInUse,
                $"The role is held by {users} user(s), {keys} API key(s) that are not revoked and {groups} member group(s). Take it from the users, revoke the keys and bind the groups to another role first.")]);
        }

        cache.Remove(id);
        await LogAsync("role.deleted", role, null, cancellationToken);
    }

    // For an export, which has checked transfer.manage: the roles of the site, without the built-in ones.
    public async Task<IReadOnlyList<RoleInfo>> SiteRolesAsync(Guid site, CancellationToken cancellationToken)
    {
        Guid? siteId = site;
        var roles = await context.Set<Role>().AsNoTracking().Where(role => role.SiteId == siteId && !role.BuiltIn).ToListAsync(cancellationToken);
        var grants = await GrantsAsync([.. roles.Select(role => role.Id)], cancellationToken);
        return [.. roles.OrderBy(role => role.Name, StringComparer.Ordinal).Select(role => Info(role, grants[role.Id]))];
    }

    // For an import into a site: the role keeps its id, and is written as roles.manage would write it. One with the same name,
    // description, flag and grants is no write, and a dry run validates and writes nothing. The caller who started the import
    // needs roles.manage in the site and every grant the role had and gets, as an edit of its own would; the write is the import's.
    public async Task<TransferOutcome> ImportAsync(RoleInfo role, Guid site, bool dryRun, OperationContext caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(caller);

        var existing = await context.Set<Role>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == role.Id, cancellationToken);
        if (existing is not null && (existing.SiteId != site || existing.BuiltIn))
        {
            throw new ValidationException([new ValidationError("id", TransferErrorCodes.IdTaken, $"The role {role.Id:D} is one of another site or a built-in role of this instance.")]);
        }

        var request = new RoleRequest(role.Name, role.Description, role.Grants) { SiteId = site, RequireTwoFactor = role.RequireTwoFactor };
        if (existing is not null)
        {
            var stored = (await GrantsAsync([existing.Id], cancellationToken))[existing.Id].ToList();

            // A role the import leaves as it is needs no validation, so a grant stored before a check refused its shape does not
            // stop an instance from importing its own export.
            if (existing.Name == role.Name && existing.Description == role.Description && existing.RequireTwoFactor == role.RequireTwoFactor && SameGrants(stored, [.. role.Grants]))
            {
                return TransferOutcome.Skipped;
            }

            var (name, description, grants) = await ValidateAsync(request, site, existing.Id, cancellationToken);
            if (existing.Name == name && existing.Description == description && existing.RequireTwoFactor == role.RequireTwoFactor && SameGrants(stored, grants))
            {
                return TransferOutcome.Skipped;
            }

            await AsCallerAsync(caller, async () =>
            {
                await authority.RequireAsync(CorePermissions.RolesManage, site, cancellationToken);
                await RequireCoveredAsync(stored, site, existing.Name, cancellationToken);
                await RequireCoveredAsync(grants, site, name, cancellationToken);
            });

            if (!dryRun)
            {
                await UpdateAsync(existing.Id, request, cancellationToken);
            }

            return TransferOutcome.Updated;
        }

        var (newName, newDescription, newGrants) = await ValidateAsync(request, site, role.Id, cancellationToken);
        await AsCallerAsync(caller, async () =>
        {
            await authority.RequireAsync(CorePermissions.RolesManage, site, cancellationToken);
            await RequireCoveredAsync(newGrants, site, newName, cancellationToken);
        });
        if (dryRun)
        {
            return TransferOutcome.Created;
        }

        var created = new Role { Id = role.Id, SiteId = site, Name = newName, Description = newDescription, RequireTwoFactor = role.RequireTwoFactor };
        context.Add(created);
        context.AddRange(Rows(created.Id, newGrants));
        await SaveAsync(created, cancellationToken);
        await LogAsync("role.created", created, null, cancellationToken);
        return TransferOutcome.Created;
    }

    // For the start of the host, as the system: the defaults this instance has not applied are given and recorded in
    // cms_default_grant, so that none is given twice and a grant an operator took away is not given again. A role that holds
    // a grant of the permission, limited or not, or a limited grant of any permission of the same plugin gets nothing new, so
    // a default never widens what an operator limited. A default that the catalogue does not hold as a permission of the
    // provider that names it is refused and not recorded. Of two starts that save at once, the second fails on the record's
    // key and reads again.
    public async Task<DefaultGrantOutcome> ApplyDefaultsAsync(IReadOnlyList<DefaultGrant> defaults, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        var refused = defaults.Where(item => catalogue.Find(item.Permission) is not { Source: PermissionSource.Plugin } || catalogue.ProviderOf(item.Permission) != item.Provider).ToList();
        List<DefaultGrant> valid = [.. defaults.Except(refused)];

        List<(Role Role, List<string> Permissions)>? given = null;
        for (var attempt = 1; given is null; attempt++)
        {
            try
            {
                given = await GiveDefaultsAsync(valid, cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                context.ChangeTracker.Clear();
                if (attempt == DefaultGrantAttempts)
                {
                    throw new InvalidOperationException($"The default grants of the permission providers were not saved after {DefaultGrantAttempts} attempts, because the built-in roles or the records of their defaults kept changing; nothing was given, and the next start tries again.", exception);
                }
            }
        }

        foreach (var (role, permissions) in given)
        {
            await LogAsync("role.updated", role, new Dictionary<string, string?> { ["added"] = string.Join("; ", permissions), ["removed"] = "" }, cancellationToken);
        }

        return new DefaultGrantOutcome([.. given.Select(item => (item.Role.Name, (IReadOnlyList<string>)item.Permissions))], refused);
    }

    private async Task<List<(Role Role, List<string> Permissions)>> GiveDefaultsAsync(List<DefaultGrant> defaults, CancellationToken cancellationToken)
    {
        List<Guid> roleIds = [.. defaults.Select(item => item.RoleId).Distinct()];
        var applied = (await context.Set<DefaultGrantRow>().AsNoTracking()
                .Where(row => roleIds.Contains(row.RoleId))
                .Select(row => new { row.RoleId, row.Permission })
                .ToListAsync(cancellationToken))
            .Select(row => (row.RoleId, row.Permission))
            .ToHashSet();

        var pending = defaults.Where(item => !applied.Contains((item.RoleId, item.Permission))).ToList();
        if (pending.Count == 0)
        {
            return [];
        }

        var roles = await context.Set<Role>().AsNoTracking().Where(role => roleIds.Contains(role.Id)).ToDictionaryAsync(role => role.Id, cancellationToken);
        var held = (await context.Set<RoleGrant>().AsNoTracking()
                .Where(grant => roleIds.Contains(grant.RoleId))
                .Select(grant => new { grant.RoleId, grant.Permission, grant.Constraints })
                .ToListAsync(cancellationToken))
            .ToLookup(grant => grant.RoleId, grant => (grant.Permission, Limited: !PermissionConstraints.FromJson(grant.Constraints).IsEmpty));

        var now = time.GetUtcNow();
        Dictionary<Guid, List<string>> given = [];
        foreach (var item in pending)
        {
            // A built-in role cannot be deleted, so only a database changed by hand lacks one; it gets nothing.
            if (!roles.ContainsKey(item.RoleId))
            {
                continue;
            }

            context.Add(new DefaultGrantRow { RoleId = item.RoleId, Permission = item.Permission, Plugin = item.Plugin, AppliedAt = now });
            var grants = held[item.RoleId];
            if (grants.Any(grant => PermissionPattern.Matches(grant.Permission, item.Permission))
                || grants.Any(grant => grant.Limited && item.PluginPermissions.Any(permission => PermissionPattern.Matches(grant.Permission, permission))))
            {
                continue;
            }

            context.Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = item.RoleId, Permission = item.Permission });
            if (!given.TryGetValue(item.RoleId, out var permissions))
            {
                given[item.RoleId] = permissions = [];
            }

            permissions.Add(item.Permission);
        }

        await context.SaveChangesAsync(cancellationToken);
        return [.. given.Select(item => (roles[item.Key], item.Value.Order(StringComparer.Ordinal).ToList()))];
    }

    private async Task AsCallerAsync(OperationContext caller, Func<Task> check)
    {
        var current = accessor.Current;
        accessor.Current = caller;
        try
        {
            await check();
        }
        finally
        {
            accessor.Current = current;
        }
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

        if (constraints.RowRule is { } rule)
        {
            if (!rules.Has(rule))
            {
                errors.Add(Invalid(path + ".constraints.rowRule", $"There is no row rule '{rule}'."));
            }
            else if (catalogue.IsGrantable(grant.Permission) && RowRuleMisfit(rule, grant.Permission) is { } misfit)
            {
                errors.Add(Invalid(path + ".constraints.rowRule", misfit));
            }
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

    // A rule limits the rows of content, so it stands only beside content permissions and for the types it is registered for,
    // each type of the schema for a pattern of every type: elsewhere the grant would read as limited and give nothing.
    private string? RowRuleMisfit(string rule, string permission) => rules.TypesWithout(rule, permission) switch
    {
        null => $"A row rule limits only content permissions, and '{permission}' is not one of them alone.",
        [] => null,
        var types => $"Row rule '{rule}' is not registered for {string.Join(", ", types.Select(type => $"'{type}'"))}, which '{permission}' grants.",
    };

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
