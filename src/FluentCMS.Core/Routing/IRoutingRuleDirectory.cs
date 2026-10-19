using FluentCMS.Core.Results;

namespace FluentCMS.Core.Routing;

// The stored routing rules of the site (RTE-01). Only a person writes them, since they decide what publishes without one (SEC-02):
// an agent or an engine is refused whatever its scope. Every write appends its audit event in the same unit of work (AUD-N1).
public interface IRoutingRuleDirectory
{
    Task<IReadOnlyList<RoutingRuleSummary>> ListAsync(CancellationToken cancellationToken = default);

    // The latest version.
    Task<Result<RoutingRule>> GetAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<RoutingRule>> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default);

    // Every active rule at the version that routes sets, by name.
    Task<IReadOnlyList<RoutingRule>> ListActiveAsync(CancellationToken cancellationToken = default);

    // Creates version 1, not active.
    Task<Result<RoutingRule>> CreateAsync(NewRoutingRule rule, CancellationToken cancellationToken = default);

    // The caller names the version it read, so a revision never silently replaces one it has not seen. The active version stays.
    Task<Result<RoutingRule>> ReviseAsync(string name, int baseVersion, RoutingRuleDefinition definition, CancellationToken cancellationToken = default);

    Task<Result<RoutingRule>> ActivateAsync(string name, int version, CancellationToken cancellationToken = default);

    Task<Result<RoutingRule>> DeactivateAsync(string name, CancellationToken cancellationToken = default);
}
