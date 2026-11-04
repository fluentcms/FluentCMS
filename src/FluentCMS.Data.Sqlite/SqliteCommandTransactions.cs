using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Data.Sqlite;

// Runs a write that EF sends alone, outside any transaction (ExecuteUpdate, ExecuteDelete, ExecuteSql), in one of its
// own. See SqliteWriteInterceptor for why.
internal sealed class SqliteCommandTransactions : DbCommandInterceptor
{
    private readonly ConditionalWeakTable<DbCommand, DbTransaction> _owned = new();

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Begin(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Begin(command);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Commit(command);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Commit(command);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Rollback(command);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Rollback(command);
        return Task.CompletedTask;
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) => Rollback(command);

    public override Task CommandCanceledAsync(DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Rollback(command);
        return Task.CompletedTask;
    }

    private static bool IsWrite(string sql)
    {
        var text = sql.AsSpan().TrimStart();
        return text.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
    }

    private void Begin(DbCommand command)
    {
        if (command.Transaction is not null || command.Connection is not { } connection || !IsWrite(command.CommandText))
        {
            return;
        }

        var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        _owned.Add(command, transaction);
    }

    private void Commit(DbCommand command)
    {
        if (_owned.TryGetValue(command, out var transaction))
        {
            _owned.Remove(command);
            using (transaction)
            {
                transaction.Commit();
            }
        }
    }

    private void Rollback(DbCommand command)
    {
        if (_owned.TryGetValue(command, out var transaction))
        {
            _owned.Remove(command);
            using (transaction)
            {
                transaction.Rollback();
            }
        }
    }
}
