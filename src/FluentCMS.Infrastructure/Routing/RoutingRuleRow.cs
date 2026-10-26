namespace FluentCMS.Infrastructure.Routing;

// A routing rule, the number of its latest version and the number of the version that routes sets; each version's definition is
// its own RoutingRuleVersionRow.
public sealed class RoutingRuleRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    // Null while the rule is not active.
    public int? ActiveVersion { get; set; }
}

// One version of a routing rule definition; inserted once and never changed, so the version a decision names stays readable.
public sealed class RoutingRuleVersionRow
{
    public Guid Id { get; set; }

    public Guid RoutingRuleId { get; set; }

    public int Version { get; set; }

    public string Definition { get; set; } = string.Empty;
}
