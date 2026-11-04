using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Data.Sqlite;

// Microsoft.Data.Sqlite waits for a busy database by resetting the statement and stepping it again every 150 ms, which
// gives up every lock between tries. In the rollback journal mode, a statement alone in autocommit then needs a moment
// with no reader at all to commit, and readers that keep coming (a runner polling for jobs) starve it until the
// timeout, when it fails with 'database is locked'. Inside a transaction, which Microsoft.Data.Sqlite begins as
// IMMEDIATE, the commit keeps the pending lock between tries, which lets the readers finish. SaveChanges is the same
// for a second reason: it writes with UPDATE ... RETURNING, which commits on a step that is not retried at all.
internal sealed class SqliteWriteInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Always(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Always(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Always(DbContext? context)
    {
        if (context is not null)
        {
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        }
    }
}
