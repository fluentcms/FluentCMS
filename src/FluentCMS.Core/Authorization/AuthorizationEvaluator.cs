using System.Linq.Expressions;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
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
            var grants = context.Principal.IsSystem ? [] : await GrantsAsync(context, cancellationToken);
            permissions = new EffectivePermissions(context, grants, rules);
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
        var locales = operation == ContentOperation.Read && type.Localized && permissions.RowRuleOf(type, operation, target) is null ? permissions.ReadLocalesOf(type, target) : null;
        var site = type.Scope == ContentScope.Site ? target.Site : null;
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

    private async Task<bool> MatchesAsync(SchemaType type, ContentOperation operation, Guid entry, AccessTarget target, QueryStatus status, ContentFilter rule, CancellationToken cancellationToken)
    {
        var state = operation == ContentOperation.Read && status == QueryStatus.Published ? ContentState.Published : ContentState.Draft;
        var scope = new QueryScope(database, schema, dialect, new ContentQueryAccess.RuleOnly(type, rule), options, state, target.RowLocale ?? target.Locale ?? "", target.Site);

        var row = Expression.Parameter(typeof(QueryRow), "row");
        var condition = new FilterCompiler(scope).Where(type, null, row)!;
        scope.ThrowIfInvalid();

        var rows = scope.Rows(type)
            .Where(item => item.Document.DocumentId == entry)
            .Where(Expression.Lambda<Func<QueryRow, bool>>(condition, row));

        return await (ReachesTrash(operation) ? rows.IgnoreQueryFilters() : rows).AnyAsync(cancellationToken);
    }

    // These two work on trashed entries, which the query filter on Document otherwise hides.
    private static bool ReachesTrash(ContentOperation operation) => operation is ContentOperation.Restore or ContentOperation.Purge;

    private async Task<List<EffectiveGrant>> GrantsAsync(OperationContext context, CancellationToken cancellationToken)
    {
        List<Guid> ids = context.Principal.IsAnonymous
            ? [await PublicRoleAsync(context, cancellationToken)]
            : [.. await principalRoles.RolesOfAsync(context.Principal, cancellationToken)];

        // Stamps first: a role that changes between the two reads is then cached under its older stamp, and the next
        // request reloads it. The other order would keep the old grants under the new stamp.
        var roles = await database.Set<Role>().AsNoTracking()
            .Where(role => ids.Contains(role.Id))
            .Select(role => new { role.Id, role.PermissionsVersion, role.SiteId })
            .ToListAsync(cancellationToken);

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

    // A site's own Public role, if it has one (#2691), and otherwise the seeded instance-wide one.
    private async Task<Guid> PublicRoleAsync(OperationContext context, CancellationToken cancellationToken)
    {
        var site = context.Site.Value;
        var own = await database.Set<Role>().AsNoTracking()
            .Where(role => role.SiteId == site && role.Name == BuiltInRoles.PublicName)
            .Select(role => (Guid?)role.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return own ?? BuiltInRoles.Public;
    }
}
