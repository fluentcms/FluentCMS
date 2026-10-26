using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Rules;

// The rule book of the site (RUL-01). Every write stores a new version, or proposes a retirement, as an operation of the change set
// it names, with their audit events in one unit of work; only publishing that set changes which version is active. Only a person
// writes rules (M1: any administrator), so an agent or an engine is refused.
public interface IRuleDirectory
{
    // Every rule at its latest version, in slug order, with its state and the version that is active.
    Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken = default);

    // The latest version, which a revision builds on.
    Task<Result<Rule>> GetAsync(string slug, CancellationToken cancellationToken = default);

    // The active version, which agents and checks follow; rule_not_active for a draft or a retired rule.
    Task<Result<Rule>> GetActiveAsync(string slug, CancellationToken cancellationToken = default);

    // Earlier versions stay readable after a revision.
    Task<Result<Rule>> GetVersionAsync(string slug, int version, CancellationToken cancellationToken = default);

    Task<Result<Rule>> CreateAsync(NewRule rule, ChangeSetTarget changeSet, CancellationToken cancellationToken = default);

    // From the version the caller read: the latest one, the active one, or the one the named set already proposes.
    Task<Result<Rule>> ReviseAsync(string slug, int baseVersion, RuleDefinition definition, ChangeSetTarget changeSet, CancellationToken cancellationToken = default);

    // Proposes taking the active version out of the rule book; the rule is retired once the set is published, and reverting that
    // set makes it active again.
    Task<Result<Rule>> RetireAsync(string slug, ChangeSetTarget changeSet, CancellationToken cancellationToken = default);
}
