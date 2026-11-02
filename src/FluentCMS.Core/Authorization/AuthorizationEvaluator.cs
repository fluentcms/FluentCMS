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

        if (permissions.RowRuleOf(type, operation, target) is not { } rule)
        {
            return true;
        }

        var state = operation == ContentOperation.Read && status == QueryStatus.Published ? ContentState.Published : ContentState.Draft;
        var scope = new QueryScope(database, schema, dialect, new ContentQueryAccess.RuleOnly(type, rule), options, state, target.Locale ?? "");

        var row = Expression.Parameter(typeof(QueryRow), "row");
        var condition = new FilterCompiler(scope).Where(type, null, row)!;
        scope.ThrowIfInvalid();

        return await scope.Rows(type)
            .Where(item => item.Document.DocumentId == entry)
            .Where(Expression.Lambda<Func<QueryRow, bool>>(condition, row))
            .AnyAsync(cancellationToken);
    }

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
        var stale = roles.Where(role => !cache.TryGet(role.Id, role.PermissionsVersion, out _)).ToList();
        foreach (var role in roles.Except(stale))
        {
            cache.TryGet(role.Id, role.PermissionsVersion, out var cached);
            grants.AddRange(cached);
        }

        if (stale.Count > 0)
        {
            var staleIds = stale.Select(role => role.Id).ToList();
            var stored = await database.Set<RoleGrant>().AsNoTracking()
                .Where(grant => staleIds.Contains(grant.RoleId))
                .ToListAsync(cancellationToken);
            var byRole = stored.ToLookup(grant => grant.RoleId);

            foreach (var role in stale)
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
