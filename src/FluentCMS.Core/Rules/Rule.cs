using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Rules;

// RUL-03. A rule is a draft until a change set publishes one of its versions, active while a published version is live, and
// retired once a change set retires it. Only publishing moves it between them.
public enum RuleState
{
    Draft,
    Active,
    Retired,
}

public sealed record NewRule(string Slug, RuleDefinition Definition);

// A rule of the rule book (RUL-01): a slug and a versioned definition, scoped to a site. A revision makes the next version; the
// id, site and slug stay, because the checks proposed from a rule and the change sets that hold it point at them.
public sealed class Rule
{
    private Rule(Guid id, Guid siteId, string slug, int version, RuleDefinition definition, int? activeVersion, RuleState state)
    {
        Id = id;
        SiteId = siteId;
        Slug = slug;
        Version = version;
        Definition = definition;
        ActiveVersion = activeVersion;
        State = state;
    }

    public Guid Id { get; }

    public Guid SiteId { get; }

    public string Slug { get; }

    // The latest version and its definition, which a revision builds on.
    public int Version { get; }

    public RuleDefinition Definition { get; }

    // The version a published change set made live, which agents and checks follow; null while the rule is a draft or retired.
    public int? ActiveVersion { get; }

    public RuleState State { get; }

    public static Result<Rule> Create(Guid siteId, NewRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(rule.Definition);

        var errors = new List<Error>();
        if (siteId == Guid.Empty)
        {
            errors.Add(new Error(RuleErrorCodes.SiteMissing, "A rule belongs to a site, and the site id is empty."));
        }

        if (!SchemaNames.IsSlug(rule.Slug))
        {
            errors.Add(new Error(
                RuleErrorCodes.SlugInvalid,
                $"The slug '{rule.Slug}' must be lowercase letters and digits in words joined by single hyphens, such as no-superlatives."));
        }

        errors.AddRange(rule.Definition.Validate());
        return errors.Count == 0
            ? Result<Rule>.Success(new Rule(Guid.CreateVersion7(), siteId, rule.Slug, 1, rule.Definition, null, RuleState.Draft))
            : Result<Rule>.Failure(errors);
    }

    // The next version, from the version the caller read: the latest, or the active one, so a revision never replaces a version
    // its author has not seen and can leave an unpublished draft behind.
    public Result<Rule> Revise(int baseVersion, RuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (baseVersion != Version && baseVersion != ActiveVersion)
        {
            return Result<Rule>.Failure(
                RuleErrorCodes.VersionConflict,
                $"The rule '{Slug}' is at version {Version}{(ActiveVersion is { } active ? $" with version {active} active" : string.Empty)}, so it cannot be revised from version {baseVersion}; read it again and revise from what you read.");
        }

        var errors = definition.Validate();
        return errors.Count == 0
            ? Result<Rule>.Success(new Rule(Id, SiteId, Slug, Version + 1, definition, ActiveVersion, State))
            : Result<Rule>.Failure(errors);
    }

    // Rebuilds a stored rule without checking it again, so a rule stays readable if the rules for writing one change.
    public static Rule Restore(Guid id, Guid siteId, string slug, int version, RuleDefinition definition, int? activeVersion, RuleState state)
    {
        return new Rule(id, siteId, slug, version, definition, activeVersion, state);
    }
}

public static class RuleErrorCodes
{
    public const string SiteMissing = "rule_site_missing";
    public const string SlugInvalid = "rule_slug_invalid";
    public const string SlugTaken = "rule_slug_taken";
    public const string KindUnknown = "rule_kind_unknown";
    public const string TextInvalid = "rule_text_invalid";
    public const string SeverityUnknown = "rule_severity_unknown";
    public const string ScopeInvalid = "rule_scope_invalid";
    public const string DefinitionMalformed = "rule_definition_malformed";
    public const string NotFound = "rule_not_found";
    public const string VersionNotFound = "rule_version_not_found";
    public const string VersionConflict = "rule_version_conflict";
    public const string NotActive = "rule_not_active";
    public const string PeopleOnly = "rule_people_only";
    public const string EngineOnly = "rule_engine_only";
    public const string ProposalNotFound = "rule_proposal_not_found";
    public const string ProposalDecided = "rule_proposal_decided";
    public const string CompileReplaced = "rule_compile_replaced";
}
