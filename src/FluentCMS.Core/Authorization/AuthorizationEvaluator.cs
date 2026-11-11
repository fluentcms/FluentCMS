using System.Linq.Expressions;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Authorization;

internal sealed class AuthorizationEvaluator(
    CmsDbContext database,
    SchemaModel schema,
    IJsonSqlDialect dialect,
    ContentQueryOptions options,
    IPrincipalRoles principalRoles,
    EffectiveGrantCache cache,
    RowRuleRegistry rules) : IAuthorizationEvaluator
{
    // By reference: two principals with one id are equal even when their claims, and so their scopes, differ.
    private readonly Dictionary<OperationContext, EffectivePermissions> _resolved = new(ReferenceEqualityComparer.Instance);

    public async ValueTask<EffectivePermissions> ResolveAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_resolved.TryGetValue(context, out var permissions))
        {
            if (context.Principal.IsSystem)
            {
                permissions = new EffectivePermissions(context, [], rules);
            }
            else if (context.Principal.Kind == PrincipalKind.Member)
            {
                // The row rules of the operation read the principal with the groups it has now.
                var (roles, groups) = await MemberRolesAsync(context.Principal.Id, context.Site.Value, cancellationToken);
                permissions = new EffectivePermissions(context with { Principal = InGroups(context.Principal, groups) }, await GrantsOfAsync(roles, cancellationToken), rules);
            }
            else
            {
                var (grants, clientGrants) = await GrantsAsync(context, cancellationToken);
                permissions = new EffectivePermissions(context, grants, rules, clientGrants is null ? null : new EffectivePermissions(context, clientGrants, rules));
            }

            _resolved[context] = permissions;
        }

        return permissions;
    }

    public async ValueTask<bool> CanAccessEntryAsync(
        OperationContext context,
        SchemaType type,
        ContentOperation operation,
        Guid entry,
        AccessTarget target,
        QueryStatus status = QueryStatus.Published,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var permissions = await ResolveAsync(context, cancellationToken);
        if (!permissions.Allows(type, operation, target))
        {
            return false;
        }

        if (operation == ContentOperation.Read && status == QueryStatus.Draft && !permissions.CanReadDraft(type, target))
        {
            return false;
        }

        if (!await IsInReachAsync(type, operation, entry, target, permissions, cancellationToken))
        {
            return false;
        }

        return permissions.RowRuleOf(type, operation, target) is not { } rule
            || await MatchesAsync(type, operation, entry, target, status, rule, cancellationToken);
    }

    public async ValueTask<IReadOnlySet<Guid>> AccessibleEntriesAsync(
        OperationContext context,
        SchemaType type,
        ContentOperation operation,
        IReadOnlyCollection<Guid> entries,
        AccessTarget target,
        QueryStatus status = QueryStatus.Published,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(entries);

        var permissions = await ResolveAsync(context, cancellationToken);
        if (entries.Count == 0
            || !permissions.Allows(type, operation, target)
            || (operation == ContentOperation.Read && status == QueryStatus.Draft && !permissions.CanReadDraft(type, target)))
        {
            return new HashSet<Guid>();
        }

        var reached = await InReachAsync(type, operation, entries, target, permissions, cancellationToken);
        if (reached.Count == 0 || permissions.RowRuleOf(type, operation, target) is not { } rule)
        {
            return reached;
        }

        return await MatchingAsync(type, operation, reached, target, status, rule, cancellationToken);
    }

    public async ValueTask<WritableFields> WritableFieldsAsync(
        OperationContext context,
        SchemaType type,
        ContentOperation operation,
        Guid? entry,
        AccessTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var permissions = await ResolveAsync(context, cancellationToken);
        if (!permissions.Allows(type, operation, target))
        {
            return WritableFields.None;
        }

        if (entry is { } inSite && !await IsInReachAsync(type, operation, inSite, target, permissions, cancellationToken))
        {
            return WritableFields.None;
        }

        if (permissions.IsSystem)
        {
            return WritableFields.All;
        }

        var writable = await WritableByAsync(permissions, type, operation, entry, target, cancellationToken);
        return permissions.Limit is { } limit && writable.Allowed
            ? writable.And(await WritableByAsync(limit, type, operation, entry, target, cancellationToken))
            : writable;
    }

    // What the grants of one set, the principal's or the role's of its client, let a write touch.
    private async Task<WritableFields> WritableByAsync(EffectivePermissions permissions, SchemaType type, ContentOperation operation, Guid? entry, AccessTarget target, CancellationToken cancellationToken)
    {
        var allowed = false;
        HashSet<string> fields = new(StringComparer.Ordinal);
        Dictionary<string, bool> matched = new(StringComparer.Ordinal);

        foreach (var grant in permissions.GrantsOf(type, operation, target))
        {
            if ((operation != ContentOperation.Create || entry is not null) && grant.Constraints.RowRule is { } name)
            {
                if (!matched.TryGetValue(name, out var matches))
                {
                    matches = entry is { } id
                        && permissions.RuleFilter(name, type) is { } rule
                        && await MatchesAsync(type, operation, id, target, QueryStatus.Draft, rule, cancellationToken);
                    matched[name] = matches;
                }

                if (!matches)
                {
                    continue;
                }
            }

            var reachesShared = !type.Localized || grant.ReachesEveryLocale(target);
            if (grant.Constraints.Fields is null && reachesShared)
            {
                return WritableFields.All;
            }

            allowed = true;
            var names = grant.Constraints.Fields ?? type.Fields.Select(field => field.Name);
            fields.UnionWith(reachesShared ? names : names.Where(name => type.FindField(name) is not { Localizable: false }));
        }

        return allowed ? new WritableFields(true, fields) : WritableFields.None;
    }

    // A grant's site limit is about the entry, so the entry's own site must be the one asked about. A global type's
    // entries belong to no site, and a target of every site has nothing to compare. A read's locale limit is about the entry
    // too: one with rows only in other locales reads as no entry. A row rule answers that itself, since it reads the row in
    // the locale asked about. A write may add a locale, so this is for reads alone.
    private async Task<bool> IsInReachAsync(SchemaType type, ContentOperation operation, Guid entry, AccessTarget target, EffectivePermissions permissions, CancellationToken cancellationToken)
    {
        var (site, locales) = ReachOf(type, operation, target, permissions);
        if (site is null && locales is null)
        {
            return true;
        }

        var typeName = type.Name;
        IQueryable<Document> documents = ReachesTrash(operation) ? database.Documents.IgnoreQueryFilters() : database.Documents;
        documents = documents.AsNoTracking().Where(document => document.DocumentId == entry && document.Type == typeName);
        if (site is { } one)
        {
            documents = documents.Where(document => document.SiteId == one.Value);
        }

        if (locales is null)
        {
            return await documents.AnyAsync(cancellationToken);
        }

        return (await documents.Select(document => document.Locale).ToListAsync(cancellationToken)).Any(locales.Contains);
    }

    // Which of the entries are in reach, in one query.
    private async Task<HashSet<Guid>> InReachAsync(SchemaType type, ContentOperation operation, IReadOnlyCollection<Guid> entries, AccessTarget target, EffectivePermissions permissions, CancellationToken cancellationToken)
    {
        HashSet<Guid> ids = [.. entries];
        var (site, locales) = ReachOf(type, operation, target, permissions);
        if (site is null && locales is null)
        {
            return ids;
        }

        var typeName = type.Name;
        List<Guid> asked = [.. ids];
        IQueryable<Document> documents = ReachesTrash(operation) ? database.Documents.IgnoreQueryFilters() : database.Documents;
        documents = documents.AsNoTracking().Where(document => asked.Contains(document.DocumentId) && document.Type == typeName);
        if (site is { } one)
        {
            documents = documents.Where(document => document.SiteId == one.Value);
        }

        if (locales is null)
        {
            return [.. await documents.Select(document => document.DocumentId).ToListAsync(cancellationToken)];
        }

        var rows = await documents.Select(document => new { document.DocumentId, document.Locale }).ToListAsync(cancellationToken);
        return [.. rows.Where(row => locales.Contains(row.Locale)).Select(row => row.DocumentId)];
    }

    // An entry the caller found in the target's site, outside the trash, is known to be in that site.
    private static (SiteId? Site, IReadOnlySet<string>? Locales) ReachOf(SchemaType type, ContentOperation operation, AccessTarget target, EffectivePermissions permissions)
    {
        var locales = operation == ContentOperation.Read && type.Localized && permissions.RowRuleOf(type, operation, target) is null ? permissions.ReadLocalesOf(type, target) : null;
        var site = type.Scope != ContentScope.Site || (target.Found && !ReachesTrash(operation)) ? null : target.Site;
        return (site, locales);
    }

    private async Task<bool> MatchesAsync(SchemaType type, ContentOperation operation, Guid entry, AccessTarget target, QueryStatus status, ContentFilter rule, CancellationToken cancellationToken)
    {
        var rows = RuleRows(type, operation, target, status, rule).Where(item => item.Document.DocumentId == entry);
        return await (ReachesTrash(operation) ? rows.IgnoreQueryFilters() : rows).AnyAsync(cancellationToken);
    }

    // Which of the entries have a row the rule lets through, in one query.
    private async Task<HashSet<Guid>> MatchingAsync(SchemaType type, ContentOperation operation, IReadOnlyCollection<Guid> entries, AccessTarget target, QueryStatus status, ContentFilter rule, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. entries];
        var rows = RuleRows(type, operation, target, status, rule).Where(item => ids.Contains(item.Document.DocumentId));
        return [.. await (ReachesTrash(operation) ? rows.IgnoreQueryFilters() : rows).Select(item => item.Document.DocumentId).ToListAsync(cancellationToken)];
    }

    private IQueryable<QueryRow> RuleRows(SchemaType type, ContentOperation operation, AccessTarget target, QueryStatus status, ContentFilter rule)
    {
        var state = operation == ContentOperation.Read && status == QueryStatus.Published ? ContentState.Published : ContentState.Draft;
        var scope = new QueryScope(database, schema, dialect, new ContentQueryAccess.RuleOnly(type, rule), options, state, target.RowLocale ?? target.Locale ?? "", target.Site);

        var row = Expression.Parameter(typeof(QueryRow), "row");
        var condition = new FilterCompiler(scope).Where(type, null, row)!;
        scope.ThrowIfInvalid();

        return scope.Rows(type).Where(Expression.Lambda<Func<QueryRow, bool>>(condition, row));
    }

    // These two work on trashed entries, which the query filter on Document otherwise hides.
    private static bool ReachesTrash(ContentOperation operation) => operation is ContentOperation.Restore or ContentOperation.Purge;

    // The grants of the principal's roles and, for a token of an MCP client that registered itself, those of the role of
    // its client, which is read with the principal's roles in one query. A client role that is gone, or a claim that names
    // none, gives an empty list, which leaves the token nothing.
    private async Task<(List<EffectiveGrant> Own, List<EffectiveGrant>? Client)> GrantsAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (context.Principal.IsAnonymous)
        {
            return (await GrantsOfAsync(await PublicRoleAsync(context.Site.Value, cancellationToken), cancellationToken), null);
        }

        var own = await principalRoles.RolesOfAsync(context.Principal, cancellationToken);
        if (context.Principal.Claim(CmsClaimTypes.ClientRole) is not { } claim)
        {
            return (await GrantsOfAsync(await StampsAsync([.. own], cancellationToken), cancellationToken), null);
        }

        var clientRole = Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        var stamps = await StampsAsync([.. own.Append(clientRole).Distinct()], cancellationToken);
        return (
            await GrantsOfAsync([.. stamps.Where(stamp => own.Contains(stamp.Id))], cancellationToken),
            await GrantsOfAsync([.. stamps.Where(stamp => stamp.Id == clientRole)], cancellationToken));
    }

    // What a member holds, read on every request so that a change holds from the next one: the Public role of the site,
    // the Member role and the roles of the groups it is in, with their stamps, and the names of those groups, in one query.
    // A member that is deactivated or gone holds nothing, not even what a visitor reads; its tokens are revoked as well.
    // Nothing is taken from the token: a roles or groups claim on a member is never read.
    private async Task<(List<RoleStamp> Roles, List<string> Groups)> MemberRolesAsync(Guid memberId, Guid site, CancellationToken cancellationToken)
    {
        var rows = await (
            from role in database.Set<Role>().AsNoTracking()
            join held in database.Set<MemberGroup>().Where(item => database.Set<MemberGroupMember>().Any(membership => membership.GroupId == item.Id && membership.MemberId == memberId))
                on role.Id equals held.RoleId into bound
            from held in bound.DefaultIfEmpty()
            where database.Set<CmsMember>().Any(member => member.Id == memberId && member.DeactivatedAt == null)
                && (held != null || role.Id == BuiltInRoles.Member || role.Id == BuiltInRoles.Public || (role.SiteId == site && role.Name == BuiltInRoles.PublicName))
            select new { role.Id, role.PermissionsVersion, role.SiteId, role.Name, Group = (string?)held.Name }).ToListAsync(cancellationToken);

        // A site's own Public role stands in for the seeded one, as it does for a visitor.
        var visitor = rows.Find(row => row.SiteId == site && row.Name == BuiltInRoles.PublicName) ?? rows.Find(row => row.Id == BuiltInRoles.Public);
        List<RoleStamp> roles = [.. rows
            .Where(row => row.Group is not null || row.Id == BuiltInRoles.Member || row.Id == visitor?.Id)
            .DistinctBy(row => row.Id)
            .Select(row => new RoleStamp(row.Id, row.PermissionsVersion, row.SiteId))];
        List<string> groups = [.. rows.Select(row => row.Group).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return (roles, groups);
    }

    private static Principal InGroups(Principal member, List<string> groups)
    {
        var claims = member.Claims.Where(claim => claim.Key != Principal.GroupsClaim);
        return new Principal(member.Kind, member.Id, member.Name, groups.Count == 0 ? claims : claims.Append(KeyValuePair.Create(Principal.GroupsClaim, string.Join(' ', groups))));
    }

    private async Task<List<EffectiveGrant>> GrantsOfAsync(List<RoleStamp> roles, CancellationToken cancellationToken)
    {
        // The stamps were read first: a role that changes between the two reads is then cached under its older stamp, and
        // the next request reloads it. The other order would keep the old grants under the new stamp.
        List<EffectiveGrant> grants = [];
        List<Guid> staleIds = [];
        foreach (var role in roles)
        {
            if (cache.TryGet(role.Id, role.PermissionsVersion, out var cached))
            {
                grants.AddRange(cached);
            }
            else
            {
                staleIds.Add(role.Id);
            }
        }

        if (staleIds.Count > 0)
        {
            var stored = await database.Set<RoleGrant>().AsNoTracking()
                .Where(grant => staleIds.Contains(grant.RoleId))
                .ToListAsync(cancellationToken);
            var byRole = stored.ToLookup(grant => grant.RoleId);

            foreach (var role in roles.Where(role => staleIds.Contains(role.Id)))
            {
                var loaded = byRole[role.Id]
                    .Select(grant => new EffectiveGrant(grant.Permission, PermissionConstraints.FromJson(grant.Constraints), role.SiteId))
                    .ToList();
                cache.Set(role.Id, role.PermissionsVersion, loaded);
                grants.AddRange(loaded);
            }
        }

        return grants;
    }

    private Task<List<RoleStamp>> StampsAsync(List<Guid> ids, CancellationToken cancellationToken) =>
        database.Set<Role>().AsNoTracking()
            .Where(role => ids.Contains(role.Id))
            .Select(role => new RoleStamp(role.Id, role.PermissionsVersion, role.SiteId))
            .ToListAsync(cancellationToken);

    // A site's own Public role, if it has one (#2691), and otherwise the seeded instance-wide one. Which one it is stays
    // known while its stamp does, so a warm request reads the stamp alone; a stamp that moved or a role that is gone looks
    // again, in one query with the stamps.
    private async Task<List<RoleStamp>> PublicRoleAsync(Guid site, CancellationToken cancellationToken)
    {
        if (cache.TryGetPublic(site, out var known))
        {
            var stamps = await StampsAsync([known.Role], cancellationToken);
            if (stamps is [{ } stamp] && stamp.PermissionsVersion == known.Version)
            {
                return stamps;
            }
        }

        var found = await database.Set<Role>().AsNoTracking()
            .Where(role => (role.SiteId == site && role.Name == BuiltInRoles.PublicName) || role.Id == BuiltInRoles.Public)
            .Select(role => new RoleStamp(role.Id, role.PermissionsVersion, role.SiteId))
            .ToListAsync(cancellationToken);
        if ((found.Find(role => role.SiteId == site) ?? found.Find(role => role.Id == BuiltInRoles.Public)) is not { } chosen)
        {
            return [];
        }

        cache.SetPublic(site, chosen.Id, chosen.PermissionsVersion);
        return [chosen];
    }

    private sealed record RoleStamp(Guid Id, long PermissionsVersion, Guid? SiteId);
}
