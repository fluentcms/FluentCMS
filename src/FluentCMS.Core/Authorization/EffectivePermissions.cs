using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Authorization;

// Where an operation applies. A null site is every site, so only a grant with no site limit holds for it; an empty
// locale is no locale, so only a grant with no locale limit holds for it. RowLocale is the row a row rule reads when
// that is not Locale's: a write that adds a locale to an entry has no row of its own there yet.
internal readonly record struct AccessTarget(SiteId? Site, string? Locale, string? RowLocale = null);

// One row of cms_role_permission, with the site of its role: a role of one site grants nothing in another.
internal sealed record EffectiveGrant(string Pattern, PermissionConstraints Constraints, Guid? RoleSite)
{
    public bool Applies(string permission, AccessTarget target) =>
        PermissionPattern.Matches(Pattern, permission)
        && (RoleSite is null || target.Site?.Value == RoleSite)
        && (Constraints.Sites is null || (target.Site is { } site && Constraints.Sites.Contains(site)))
        && (Constraints.Locales is null || (!string.IsNullOrEmpty(target.Locale) && Constraints.Locales.Contains(target.Locale, StringComparer.OrdinalIgnoreCase)));
}

// The answers of spec 2.3 for one principal: the union of its roles' grants, narrowed by its token's scopes. Scopes
// only remove: nothing here allows what no grant does. Without a scope claim a principal has no scopes, so a token
// that lost its scopes can read published content and nothing more. Site and locale limits are applied before the
// union, so they stay tied to their grant.
internal sealed class EffectivePermissions(OperationContext context, IReadOnlyList<EffectiveGrant> grants, RowRuleRegistry rules)
{
    private readonly HashSet<string> _scopes = (context.Principal.Claim(TokenScopes.Claim) ?? "")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .ToHashSet(StringComparer.Ordinal);

    public bool IsSystem => context.Principal.IsSystem;

    // Everything beyond reading content needs a scope that says the token is for managing; a Delivery principal has none.
    public bool CanManage =>
        IsSystem
        || _scopes.Contains(TokenScopes.Management)
        || (context.Origin == Origin.Mcp && _scopes.Contains(TokenScopes.Mcp));

    public bool Allows(string permission, AccessTarget target)
    {
        ArgumentNullException.ThrowIfNull(permission);

        return IsSystem || (ScopeAllows(permission) && grants.Any(grant => grant.Applies(permission, target)));
    }

    public bool Allows(SchemaType type, ContentOperation operation, AccessTarget target) =>
        Allows(CorePermissions.Content(type.Name, operation), target);

    // The roles must allow reading the type. On top of that a Delivery principal needs a preview key or the draft
    // scope, and a managing one is bounded by its roles alone (spec 4.8).
    public bool CanReadDraft(SchemaType type, AccessTarget target) =>
        Allows(type, ContentOperation.Read, target)
        && (CanManage || _scopes.Contains(TokenScopes.Draft) || _scopes.Contains(TokenScopes.Preview));

    // The grants behind an operation, with what the scopes leave. A caller that knows the entry decides per grant.
    public IReadOnlyList<EffectiveGrant> GrantsOf(SchemaType type, ContentOperation operation, AccessTarget target)
    {
        var permission = CorePermissions.Content(type.Name, operation);
        return ScopeAllows(permission) ? [.. grants.Where(grant => grant.Applies(permission, target))] : [];
    }

    // Null when every field is readable. A response has one set of fields for all its rows, and rows are the union of
    // the grants, so a list that holds only on some rows cannot count on the others: a field is readable when a grant
    // with no row rule allows it, or when every applying grant does. This fails closed until there is a per-row
    // projection. The same answer serves select, filter, sort and population.
    public IReadOnlySet<string>? ReadableFieldsOf(SchemaType type, AccessTarget target)
    {
        if (IsSystem)
        {
            return null;
        }

        var applying = GrantsOf(type, ContentOperation.Read, target);
        if (applying.Count == 0)
        {
            return new HashSet<string>();
        }

        HashSet<string> withoutRule = new(StringComparer.Ordinal);
        HashSet<string>? inEvery = null;
        foreach (var constraints in applying.Select(grant => grant.Constraints))
        {
            if (constraints.Fields is null)
            {
                if (constraints.RowRule is null)
                {
                    return null;
                }

                continue;
            }

            if (constraints.RowRule is null)
            {
                withoutRule.UnionWith(constraints.Fields);
            }

            inEvery ??= new HashSet<string>(constraints.Fields, StringComparer.Ordinal);
            inEvery.IntersectWith(constraints.Fields);
        }

        if (inEvery is null)
        {
            return null;
        }

        withoutRule.UnionWith(inEvery);
        return withoutRule;
    }

    // An allow-list is about the fields of the type. System fields are the same for every type, and only the two that
    // identify a person are closed: they are outside the Delivery response shape and exist for row rules.
    public bool CanReadField(SchemaType type, string field, AccessTarget target)
    {
        if (!Allows(type, ContentOperation.Read, target))
        {
            return false;
        }

        if (SystemField.Find(field) is not null)
        {
            return CanManage || !SystemField.NamesAPerson(field);
        }

        return ReadableFieldsOf(type, target) is not { } fields || fields.Contains(field);
    }

    // Null when the operation reaches every row: a grant with no rule does. Rules of several grants are a union. A rule
    // this version does not have adds no rows, which is the same as what an unknown constraint does: never widen.
    public ContentFilter? RowRuleOf(SchemaType type, ContentOperation operation, AccessTarget target)
    {
        if (IsSystem)
        {
            return null;
        }

        var permission = CorePermissions.Content(type.Name, operation);
        if (!ScopeAllows(permission))
        {
            return RowRule.NoRows;
        }

        SortedSet<string> names = new(StringComparer.Ordinal);
        foreach (var grant in grants.Where(grant => grant.Applies(permission, target)))
        {
            if (grant.Constraints.RowRule is not { } name)
            {
                return null;
            }

            names.Add(name);
        }

        List<ContentFilter> filters = [];
        foreach (var name in names)
        {
            if (RuleFilter(name, type) is { } filter)
            {
                filters.Add(filter);
            }
        }

        return filters.Count switch
        {
            0 => RowRule.NoRows,
            1 => filters[0],
            _ => new Or(filters),
        };
    }

    // One rule for this operation, or null when it can match no row.
    public ContentFilter? RuleFilter(string name, SchemaType type) =>
        rules.Find(name, type.Name)?.For(context) is { } filter && !ReferenceEquals(filter, RowRule.NoRows) ? filter : null;

    private bool ScopeAllows(string permission) => CanManage || IsContentRead(permission);

    private static bool IsContentRead(string permission) =>
        permission.StartsWith(FluentCMS.Authorization.Permission.ContentPrefix + ".", StringComparison.Ordinal)
        && permission.EndsWith("." + CorePermissions.OperationName(ContentOperation.Read), StringComparison.Ordinal);
}

// What one write may touch, decided per grant against the entry. Null fields means every field.
internal sealed record WritableFields(bool Allowed, IReadOnlySet<string>? Fields)
{
    public static readonly WritableFields None = new(false, null);

    public static readonly WritableFields All = new(true, null);

    public bool Includes(string field) => Allowed && (Fields is null || Fields.Contains(field));
}
