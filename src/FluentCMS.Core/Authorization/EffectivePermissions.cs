using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Authorization;

// Where an operation applies. A null site is every site, so only a grant with no site limit holds for it; an empty
// locale is no locale, so only a grant with no locale limit holds for it. RowLocale is the row a row rule reads when
// that is not Locale's: a write that adds a locale to an entry has no row of its own there yet. EntryLocales is every
// locale the entry has or is about to have, for the fields they share; null means Locale alone. Found says that the caller
// has just read the entry's rows in Site, outside the trash, in its transaction, so that a check of that entry need not
// read them again; it is never set on a target that serves another entry.
internal readonly record struct AccessTarget(SiteId? Site, string? Locale, string? RowLocale = null, IReadOnlyCollection<string>? EntryLocales = null, bool Found = false);

// One row of cms_role_permission, with the site of its role: a role of one site grants nothing in another. The pattern is
// split when the grant is built, which is before it enters EffectiveGrantCache, since every check matches it again.
internal sealed class EffectiveGrant(string pattern, PermissionConstraints constraints, Guid? roleSite)
{
    private readonly string[]? _segments = PermissionPattern.Segments(pattern);

    public string Pattern { get; } = pattern;

    public PermissionConstraints Constraints { get; } = constraints;

    public Guid? RoleSite { get; } = roleSite;

    public bool Applies(string permission, AccessTarget target) => Constraints.Resources is null && Reaches(permission, target);

    // A grant limited to resources holds only where a plugin asks which resources it reaches, since no other check can say
    // which resource it acts on.
    public bool AppliesInSomeLocale(string permission, AccessTarget target) =>
        Constraints.Resources is null && ReachesInSomeLocale(permission, target);

    public bool Reaches(string permission, AccessTarget target) =>
        ReachesInSomeLocale(permission, target)
        && (Constraints.Locales is null || (!string.IsNullOrEmpty(target.Locale) && Constraints.Locales.Contains(target.Locale, StringComparer.OrdinalIgnoreCase)));

    private bool ReachesInSomeLocale(string permission, AccessTarget target) =>
        PermissionPattern.Matches(_segments, permission)
        && (RoleSite is null || target.Site?.Value == RoleSite)
        && (Constraints.Sites is null || (target.Site is { } site && Constraints.Sites.Contains(site)));

    // A global entry is shared by every site, so what changes it must come from a grant that no site limits.
    public bool IsInstanceWide => RoleSite is null && Constraints.Sites is null;

    // A field that is not localizable has one value for all the locales of an entry, so a grant limited to some of them
    // may change it only when that covers them all.
    public bool ReachesEveryLocale(AccessTarget target) =>
        Constraints.Locales is not { } locales
        || target.EntryLocales is null
        || target.EntryLocales.All(locale => locales.Contains(locale, StringComparer.OrdinalIgnoreCase));
}

// The answers of spec 2.3 for one principal: the union of its roles' grants, narrowed by its token's scopes. Scopes
// only remove: nothing here allows what no grant does. Without a scope claim a principal has no scopes, so a token
// that lost its scopes can read published content and nothing more. Site and locale limits are applied before the
// union, so they stay tied to their grant. A principal bound to sites or types, as an API key may be, is held to them
// on top of everything else. It lives for one request, so the content answers are kept per type, operation, site and
// locale: a list asks the same questions for every entry and field.
internal sealed class EffectivePermissions(OperationContext context, IReadOnlyList<EffectiveGrant> grants, RowRuleRegistry rules)
{
    private readonly Dictionary<Asked, IReadOnlyList<EffectiveGrant>> _grantsOf = [];

    private readonly Dictionary<Asked, IReadOnlySet<string>?> _readableFields = [];

    private readonly Dictionary<Asked, ContentFilter?> _rowRules = [];

    private readonly HashSet<string> _scopes = (context.Principal.Claim(TokenScopes.Claim) ?? "")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .ToHashSet(StringComparer.Ordinal);

    private readonly PrincipalBinding _binding = PrincipalBinding.Of(context.Principal);

    public bool IsSystem => context.Principal.IsSystem;

    // Everything beyond reading content needs a scope that says the token is for managing; a Delivery principal has none.
    public bool CanManage =>
        IsSystem
        || _scopes.Contains(TokenScopes.Management)
        || (context.Origin == Origin.Mcp && _scopes.Contains(TokenScopes.Mcp));

    public bool Allows(string permission, AccessTarget target)
    {
        ArgumentNullException.ThrowIfNull(permission);

        return IsSystem || (Permits(permission, target) && grants.Any(grant => grant.Applies(permission, target)));
    }

    public bool Allows(SchemaType type, ContentOperation operation, AccessTarget target) =>
        IsSystem || GrantsOf(type, operation, target).Count > 0;

    // The resources a permission reaches: null when a grant with no resource limit holds it, otherwise the union of the
    // resources of the grants that do, empty when none does.
    public IReadOnlySet<string>? ResourcesOf(string permission, AccessTarget target)
    {
        ArgumentNullException.ThrowIfNull(permission);

        if (IsSystem)
        {
            return null;
        }

        HashSet<string> resources = new(StringComparer.Ordinal);
        if (!Permits(permission, target))
        {
            return resources;
        }

        foreach (var grant in grants.Where(grant => grant.Reaches(permission, target)))
        {
            if (grant.Constraints.Resources is not { } limited)
            {
                return null;
            }

            resources.UnionWith(limited);
        }

        return resources;
    }

    // Whether the principal holds everything a grant gives, wherever it gives it, with no fewer limits, so that handing it to a
    // role, a user or a key gives nothing the principal does not have. Only managing principals hand anything out. A grant
    // that reaches several sites may be held through a different grant in each, but one site's locales and fields are held
    // through one grant.
    public bool Covers(GrantReach reach)
    {
        ArgumentNullException.ThrowIfNull(reach);

        if (IsSystem)
        {
            return true;
        }

        if (!CanManage)
        {
            return false;
        }

        List<GrantReach> held = [.. grants.SelectMany(grant => GrantReach.Of(grant.Pattern, grant.Constraints, grant.RoleSite, _binding))];
        return reach.Sites is null
            ? held.Any(mine => mine.Includes(reach))
            : reach.Sites.All(site => held.Any(mine => mine.Includes(reach with { Sites = new HashSet<Guid> { site } })));
    }

    // The roles must allow reading the type. On top of that a Delivery principal needs a preview key or the draft
    // scope, and a managing one is bounded by its roles alone (spec 4.8).
    public bool CanReadDraft(SchemaType type, AccessTarget target) =>
        Allows(type, ContentOperation.Read, target)
        && (CanManage || _scopes.Contains(TokenScopes.Draft) || _scopes.Contains(TokenScopes.Preview));

    // The grants behind an operation, with what the scopes leave. A caller that knows the entry decides per grant.
    public IReadOnlyList<EffectiveGrant> GrantsOf(SchemaType type, ContentOperation operation, AccessTarget target)
    {
        var asked = new Asked(type, operation, target);
        if (!_grantsOf.TryGetValue(asked, out var applying))
        {
            var permission = CorePermissions.Content(type.Name, operation);
            applying = Permits(permission, target) ? [.. Applying(type, operation, permission, target)] : [];
            _grantsOf[asked] = applying;
        }

        return applying;
    }

    private IEnumerable<EffectiveGrant> Applying(SchemaType type, ContentOperation operation, string permission, AccessTarget target)
    {
        var writesEverySite = type.Scope == ContentScope.Global && operation != ContentOperation.Read;
        return grants.Where(grant => grant.Applies(permission, target) && (!writesEverySite || (grant.IsInstanceWide && _binding.Sites is null)));
    }

    // The locales a read reaches with no row rule to ask, or null when such a grant has no locale limit. A relation's target
    // may be read in another locale than the one asked about, but a rule is only asked of that one, so a grant with a rule
    // cannot vouch for a row elsewhere.
    public IReadOnlySet<string>? ReadLocalesOf(SchemaType type, AccessTarget target)
    {
        if (IsSystem)
        {
            return null;
        }

        HashSet<string> locales = new(StringComparer.OrdinalIgnoreCase);
        var permission = CorePermissions.Content(type.Name, ContentOperation.Read);
        if (Permits(permission, target))
        {
            foreach (var grant in grants.Where(grant => grant.Constraints.RowRule is null && grant.AppliesInSomeLocale(permission, target)))
            {
                if (grant.Constraints.Locales is not { } limited)
                {
                    return null;
                }

                locales.UnionWith(limited);
            }
        }

        return locales;
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

        var asked = new Asked(type, ContentOperation.Read, target);
        if (!_readableFields.TryGetValue(asked, out var fields))
        {
            fields = ReadableFields(type, target);
            _readableFields[asked] = fields;
        }

        return fields;
    }

    private HashSet<string>? ReadableFields(SchemaType type, AccessTarget target)
    {
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
    // this version does not have adds no rows, which is the same as what an unknown constraint does: never widen. One
    // answer is one instance for the request, since the query compiler recognises the rule in a filter by reference.
    public ContentFilter? RowRuleOf(SchemaType type, ContentOperation operation, AccessTarget target)
    {
        if (IsSystem)
        {
            return null;
        }

        var asked = new Asked(type, operation, target);
        if (!_rowRules.TryGetValue(asked, out var rule))
        {
            rule = OnlyBoundEntry(RowRuleOfGrants(type, GrantsOf(type, operation, target)));
            _rowRules[asked] = rule;
        }

        return rule;
    }

    // No grant, whether the scopes or a binding left none or none applies, is no rows.
    private ContentFilter? RowRuleOfGrants(SchemaType type, IReadOnlyList<EffectiveGrant> applying)
    {
        SortedSet<string> names = new(StringComparer.Ordinal);
        foreach (var grant in applying)
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

    // A principal bound to one entry reaches no other row, whatever its grants' rules let through.
    private ContentFilter? OnlyBoundEntry(ContentFilter? rule)
    {
        if (_binding.Entry is not { } entry || ReferenceEquals(rule, RowRule.NoRows))
        {
            return rule;
        }

        var only = entry == Guid.Empty ? RowRule.NoRows : new FieldFilter("id", FilterOperator.Equal, entry);
        return rule is null ? only : new And(rule, only);
    }

    // One rule for this operation, or null when it can match no row.
    public ContentFilter? RuleFilter(string name, SchemaType type) =>
        rules.Find(name, type.Name)?.For(context) is { } filter && !ReferenceEquals(filter, RowRule.NoRows) ? filter : null;

    private bool Permits(string permission, AccessTarget target) => ScopeAllows(permission) && _binding.Allows(permission, target);

    private bool ScopeAllows(string permission) => CanManage || IsContentRead(permission);

    private static bool IsContentRead(string permission) =>
        permission.StartsWith(FluentCMS.Authorization.Permission.ContentPrefix + ".", StringComparison.Ordinal)
        && permission.EndsWith("." + CorePermissions.OperationName(ContentOperation.Read), StringComparison.Ordinal);

    // What the content answers depend on: the type's name and scope, and the target's site and locale. The row locale and
    // the entry's locales are asked about per grant by the callers that know the entry.
    private readonly record struct Asked(string Type, ContentScope Scope, ContentOperation Operation, SiteId? Site, string? Locale)
    {
        public Asked(SchemaType type, ContentOperation operation, AccessTarget target)
            : this(type.Name, type.Scope, operation, target.Site, target.Locale)
        {
        }
    }
}

// What one write may touch, decided per grant against the entry. Null fields means every field.
internal sealed record WritableFields(bool Allowed, IReadOnlySet<string>? Fields)
{
    public static readonly WritableFields None = new(false, null);

    public static readonly WritableFields All = new(true, null);

    public bool Includes(string field) => Allowed && (Fields is null || Fields.Contains(field));
}
