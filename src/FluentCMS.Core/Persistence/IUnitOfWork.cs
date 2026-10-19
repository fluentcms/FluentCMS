namespace FluentCMS.Core.Persistence;

// A change and the audit event that describes it commit together or not at all (AUD-N1, ADR-0005).
public interface IUnitOfWork
{
    // Nested calls join the outer unit of work; only the outermost scope commits the transaction.
    Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken = default);

    // Runs the step once the outermost scope ends without committing, in a unit of work of its own, for what must outlive a
    // rolled-back change, such as the security event of a refusal (SEC-08). Without an open scope, or once it commits, it never runs.
    void AfterRollback(Func<CancellationToken, Task> step);
}

public interface IUnitOfWorkScope : IAsyncDisposable
{
    // Disposing a scope that was not committed rolls back everything, including work committed by inner scopes.
    Task CommitAsync(CancellationToken cancellationToken = default);
}
