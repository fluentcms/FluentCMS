using FluentCMS.Core.Results;

namespace FluentCMS.Core.Checks;

// The stored checks of the site (CHK-02). Every write appends its audit event in the same unit of work (AUD-N1).
public interface ICheckDirectory
{
    Task<IReadOnlyList<CheckSummary>> ListAsync(CancellationToken cancellationToken = default);

    // The latest version.
    Task<Result<Check>> GetAsync(string name, CancellationToken cancellationToken = default);

    Task<Result<Check>> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default);

    // Every active check at the version that runs, by name. A check confirmed from a rule's proposal is left out while the rule is not
    // active at the version it was proposed for (RUL-04).
    Task<IReadOnlyList<Check>> ListActiveAsync(CancellationToken cancellationToken = default);

    // Creates version 1, not active.
    Task<Result<Check>> CreateAsync(NewCheck check, CancellationToken cancellationToken = default);

    // The caller names the version it read, so a revision never silently replaces one it has not seen. The active version stays.
    Task<Result<Check>> ReviseAsync(string name, int baseVersion, CheckDefinition definition, CancellationToken cancellationToken = default);

    // Validates the version against the installed check types and runs its examples; only if every example gets its verdict does it
    // become the version that runs. Only a person activates a check (D17).
    Task<Result<Check>> ActivateAsync(string name, int version, CancellationToken cancellationToken = default);

    // Only a person deactivates a check, since that stops it from guarding content.
    Task<Result<Check>> DeactivateAsync(string name, CancellationToken cancellationToken = default);
}
