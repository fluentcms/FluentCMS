using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Data.SqlServer;

// SqlClient runs a command whose transaction has ended, rolled back by a failed write without a savepoint (MARS) or by
// the server (a deadlock victim), outside any transaction, so a write that went on after the failure would commit on
// its own. The other drivers refuse such a command, and so does this (#2711).
internal sealed class EndedTransactionGuard : DbCommandInterceptor
{
    public static readonly EndedTransactionGuard Instance = new();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) =>
        Checked(eventData, result);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Checked(eventData, result));

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
        Checked(eventData, result);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Checked(eventData, result));

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) =>
        Checked(eventData, result);

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Checked(eventData, result));

    // EF's transaction, because SqlCommand.Transaction reads null once the transaction has ended.
    private static T Checked<T>(CommandEventData eventData, T result) =>
        eventData.Context?.Database.CurrentTransaction?.GetDbTransaction() is { Connection: null }
            ? throw new InvalidOperationException("The transaction of this command has ended, rolled back by a write that failed in it or by the server; roll it back or dispose it before writing again.")
            : result;
}
