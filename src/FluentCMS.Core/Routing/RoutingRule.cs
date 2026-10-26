using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Routing;

// A routing rule of the site under a name, at one of its versions (RTE-01). Revising makes the next version and leaves the active one
// as it is, so what routes sets is always a version a person activated. Until the rule book exists (F-RUL), a person writes these
// directly; afterwards a published permission and routing rule compiles to one.
public sealed class RoutingRule
{
    private RoutingRule(Guid id, Guid siteId, string name, int version, int latestVersion, int? activeVersion, RoutingRuleDefinition definition)
    {
        Id = id;
        SiteId = siteId;
        Name = name;
        Version = version;
        LatestVersion = latestVersion;
        ActiveVersion = activeVersion;
        Definition = definition;
    }

    public Guid Id { get; }

    public Guid SiteId { get; }

    public string Name { get; }

    // The version this definition belongs to.
    public int Version { get; }

    public int LatestVersion { get; }

    // The version that routes sets; null while the rule is not active.
    public int? ActiveVersion { get; }

    public RoutingRuleDefinition Definition { get; }

    public static Result<RoutingRule> Create(Guid siteId, string name, RoutingRuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<Error>();

        if (siteId == Guid.Empty)
        {
            errors.Add(new Error(RoutingErrorCodes.SiteMissing, "A routing rule belongs to a site, and the site id is empty."));
        }

        // Named like a content type, such as "price-changes".
        if (!SchemaNames.IsSlug(name))
        {
            errors.Add(new Error(
                RoutingErrorCodes.NameInvalid,
                $"The name '{name}' must be lowercase letters and digits in words joined by single hyphens, such as 'price-changes'."));
        }

        errors.AddRange(definition.Validate().Errors);

        return errors.Count == 0
            ? Result<RoutingRule>.Success(new RoutingRule(Guid.CreateVersion7(), siteId, name, 1, 1, null, definition))
            : Result<RoutingRule>.Failure(errors);
    }

    // Only called on the latest version, which the next one follows.
    public Result<RoutingRule> Revise(RoutingRuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var valid = definition.Validate();
        return valid.IsSuccess
            ? Result<RoutingRule>.Success(new RoutingRule(Id, SiteId, Name, LatestVersion + 1, LatestVersion + 1, ActiveVersion, definition))
            : Result<RoutingRule>.Failure(valid.Errors);
    }

    public RoutingRule WithActiveVersion(int? activeVersion)
    {
        return new RoutingRule(Id, SiteId, Name, Version, LatestVersion, activeVersion, Definition);
    }

    // Rebuilds a stored rule without validating again, so it stays readable if the rules for definitions change.
    public static RoutingRule Restore(
        Guid id,
        Guid siteId,
        string name,
        int version,
        int latestVersion,
        int? activeVersion,
        RoutingRuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        return new RoutingRule(id, siteId, name, version, latestVersion, activeVersion, definition);
    }
}
