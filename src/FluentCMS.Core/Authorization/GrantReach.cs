namespace FluentCMS.Core.Authorization;

// What one grant gives once its role's site, its constraints and a principal's binding are applied: the permissions of a
// pattern, in sites (null for every site) and locales and fields (null for all), on the rows of a rule (null for every row).
internal sealed record GrantReach(string Pattern, IReadOnlySet<Guid>? Sites, IReadOnlyList<string>? Locales, IReadOnlyList<string>? Fields, string? RowRule)
{
    private const string ContentPrefix = FluentCMS.Authorization.Permission.ContentPrefix + ".";

    // A grant that reaches no site, or no permission a type binding leaves, gives nothing and has no reach.
    public static IEnumerable<GrantReach> Of(string pattern, PermissionConstraints constraints, Guid? roleSite, PrincipalBinding binding)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(binding);

        var sites = Intersect(Intersect(roleSite is { } site ? new HashSet<Guid> { site } : null, constraints.Sites?.Select(item => item.Value)), binding.Sites);
        if (sites is { Count: 0 })
        {
            return [];
        }

        IEnumerable<string> patterns = binding.Types is { } types
            ? types.Select(type => PermissionPattern.Intersect(pattern, ContentPrefix + type + ".*")).OfType<string>()
            : [pattern];

        return [.. patterns.Distinct(StringComparer.Ordinal).Select(narrowed => new GrantReach(narrowed, sites, constraints.Locales, constraints.Fields, constraints.RowRule))];
    }

    // Whether this gives everything the other gives, with no fewer limits.
    public bool Includes(GrantReach other) =>
        PermissionPattern.Covers(Pattern, other.Pattern)
        && (Sites is null || (other.Sites is not null && other.Sites.IsSubsetOf(Sites)))
        && (Locales is null || (other.Locales is not null && other.Locales.All(locale => Locales.Contains(locale, StringComparer.OrdinalIgnoreCase))))
        && (Fields is null || (other.Fields is not null && other.Fields.All(field => Fields.Contains(field, StringComparer.Ordinal))))
        && (RowRule is null || string.Equals(RowRule, other.RowRule, StringComparison.Ordinal));

    private static HashSet<Guid>? Intersect(IEnumerable<Guid>? left, IEnumerable<Guid>? right)
    {
        if (left is null)
        {
            return right?.ToHashSet();
        }

        var set = left.ToHashSet();
        if (right is not null)
        {
            set.IntersectWith(right);
        }

        return set;
    }
}
