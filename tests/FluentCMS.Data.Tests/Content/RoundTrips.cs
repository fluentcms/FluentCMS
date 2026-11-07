using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Data.Tests.Content;

// Counts what a context sends to the database while armed: every command, and every transaction begun, committed or
// rolled back. Each is one round trip. A save is one too, as on a provider that batches it: SQLite sends each row alone.
internal sealed class RoundTrips : DbCommandInterceptor, IDbTransactionInterceptor, ISaveChangesInterceptor
{
    private readonly List<string> _sent = [];
    private int _saving;

    public bool Armed { get; set; }

    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public int Count => Sent.Count;

    public void Reset()
    {
        lock (_sent)
        {
            _sent.Clear();
        }
    }

    public async Task<T> CountAsync<T>(Func<Task<T>> work)
    {
        Reset();
        Armed = true;
        try
        {
            return await work();
        }
        finally
        {
            Armed = false;
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Add(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Add(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Add(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Add(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Add("-- begin");
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Add("-- begin");
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Add("-- commit");

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Add("-- commit");
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Add("-- rollback");

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Add("-- rollback");
        return Task.CompletedTask;
    }

    public void CreatedSavepoint(DbTransaction transaction, TransactionEventData eventData) => Add("-- savepoint");

    public Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        Add("-- savepoint");
        return Task.CompletedTask;
    }

    public void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData) => Add("-- release");

    public Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        Add("-- release");
        return Task.CompletedTask;
    }

    public void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) => Add("-- rollback to savepoint");

    public Task RolledBackToSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        Add("-- rollback to savepoint");
        return Task.CompletedTask;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        StartSave();
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        StartSave();
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Interlocked.Decrement(ref _saving);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Interlocked.Decrement(ref _saving);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Interlocked.Decrement(ref _saving);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Decrement(ref _saving);
        return Task.CompletedTask;
    }

    public void SaveChangesCanceled(DbContextEventData eventData) => Interlocked.Decrement(ref _saving);

    public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Decrement(ref _saving);
        return Task.CompletedTask;
    }

    private void StartSave()
    {
        Add("-- save");
        Interlocked.Increment(ref _saving);
    }

    private void Add(string text)
    {
        if (!Armed)
        {
            return;
        }

        lock (_sent)
        {
            if (Volatile.Read(ref _saving) > 0 && !text.StartsWith("--", StringComparison.Ordinal))
            {
                return;
            }

            _sent.Add(text);
        }
    }
}
