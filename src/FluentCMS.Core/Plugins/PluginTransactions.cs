using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Core.Plugins;

// The plugin contexts of a scope that began or joined a transaction, so that the transaction probe sees a plugin's open
// transaction as it sees one on the core contexts. Core adds it to every PluginDbContext<T> it registers.
internal sealed class PluginTransactions : DbTransactionInterceptor
{
    private readonly Lock _gate = new();
    private readonly HashSet<DbContext> _contexts = [];

    public bool InTransaction
    {
        get
        {
            lock (_gate)
            {
                return _contexts.Any(Open);
            }
        }
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Track(eventData.Context);
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Track(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        Track(eventData.Context);
        return result;
    }

    public override ValueTask<DbTransaction> TransactionUsedAsync(DbConnection connection, TransactionEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Track(eventData.Context);
        return ValueTask.FromResult(result);
    }

    // A context the plugin disposed before its scope ended holds no transaction.
    private static bool Open(DbContext context)
    {
        try
        {
            return context.Database.CurrentTransaction is not null;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void Track(DbContext? context)
    {
        if (context is not null)
        {
            lock (_gate)
            {
                _contexts.Add(context);
            }
        }
    }
}
