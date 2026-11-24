using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Data.SqlServer;

// The transaction of a content write, whose session has the SET LOCK_TIMEOUT of BeginWriteAsync. The setting outlives the
// transaction in the session, which the context keeps while its connection stays open, so it is set back to wait without end
// as the last statement before the commit, and before a rollback or disposal of a transaction that did not commit. A pooled
// connection is reset as it is opened again, as if it logged in anew, which ends the setting too where the set back failed.
internal sealed class LockTimeoutTransaction(DbContext context, IDbContextTransaction inner) : IDbContextTransaction
{
    private const string WaitWithoutEnd = "SET LOCK_TIMEOUT -1";

    private bool _setBack;

    public Guid TransactionId => inner.TransactionId;

    public bool SupportsSavepoints => inner.SupportsSavepoints;

    public void Commit()
    {
        context.Database.ExecuteSqlRaw(WaitWithoutEnd);
        _setBack = true;
        inner.Commit();
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlRawAsync(WaitWithoutEnd, cancellationToken);
        _setBack = true;
        await inner.CommitAsync(cancellationToken);
    }

    public void Rollback()
    {
        TrySetBackAsync().AsTask().GetAwaiter().GetResult();
        inner.Rollback();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await TrySetBackAsync();
        await inner.RollbackAsync(cancellationToken);
    }

    public void Dispose()
    {
        TrySetBackAsync().AsTask().GetAwaiter().GetResult();
        inner.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await TrySetBackAsync();
        await inner.DisposeAsync();
    }

    public void CreateSavepoint(string name) => inner.CreateSavepoint(name);

    public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) => inner.CreateSavepointAsync(name, cancellationToken);

    public void RollbackToSavepoint(string name) => inner.RollbackToSavepoint(name);

    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) => inner.RollbackToSavepointAsync(name, cancellationToken);

    public void ReleaseSavepoint(string name) => inner.ReleaseSavepoint(name);

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) => inner.ReleaseSavepointAsync(name, cancellationToken);

    // On the driver's connection, past EndedTransactionGuard: a write that failed may have ended the transaction, as a deadlock
    // victim's is, and the setting is the session's either way. A connection that fails it, broken or still held by a hook left
    // running, keeps the setting until it is reset in the pool, and what the write failed with is what the caller gets.
    private async ValueTask TrySetBackAsync()
    {
        if (_setBack)
        {
            return;
        }

        _setBack = true;
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = WaitWithoutEnd;
            command.Transaction = inner.GetDbTransaction() is { Connection: not null } live ? live : null;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (DbException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
