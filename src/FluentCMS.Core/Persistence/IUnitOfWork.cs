namespace FluentCMS.Core.Persistence;

// A change and the audit event that describes it commit together or not at all (AUD-N1, ADR-0005).
public interface IUnitOfWork
{
    // Nested calls join the outer unit of work; only the outermost scope commits the transaction.
    Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkScope : IAsyncDisposable
{
    // Disposing a scope that was not committed rolls back everything, including work committed by inner scopes.
    Task CommitAsync(CancellationToken cancellationToken = default);
}
