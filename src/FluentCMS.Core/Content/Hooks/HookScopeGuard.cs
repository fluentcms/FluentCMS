using System.Data.Common;
using FluentCMS.Content;
using FluentCMS.Hooks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Core.Content.Hooks;

// Marks a scope that left a hook running, and from then on lets its CmsDbContext save and commit nothing: the hook still
// holds the context, and what it or a write nested in it sends would commit with another write, or on its own once the
// transaction it ran in has ended. It is asked again at each command and commit, for a save that began before the mark.
// A write sent as a query, or on the connection or the driver's transaction, does not pass it (spec 4.4).
internal sealed class HookScopeGuard : DbCommandInterceptor, ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private volatile string? _hook;

    public bool LeftRunning => _hook is not null;

    // The first hook left is the one named.
    public void MarkLeftRunning(string hook) => _hook ??= hook;

    public void ThrowIfLeftRunning()
    {
        if (_hook is { } hook)
        {
            throw new HookRejectedException(
                ContentErrorCodes.HookTimedOut,
                $"The hook {hook} ran past its time or was left by a cancelled caller and may still be running in this scope, so nothing more is written or committed in it.");
        }
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) => Checked(result);

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Checked(result));

    public InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) => Checked(result);

    public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Checked(result));

    public InterceptionResult ReleasingSavepoint(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) => Checked(result);

    public ValueTask<InterceptionResult> ReleasingSavepointAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Checked(result));

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) =>
        CheckedWrite(eventData, result);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(CheckedWrite(eventData, result));

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) =>
        CheckedWrite(eventData, result);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(CheckedWrite(eventData, result));

    private T Checked<T>(T result)
    {
        ThrowIfLeftRunning();
        return result;
    }

    // By what sent the command, not its text, which differs by provider. Reads go on: the scope may still answer with one.
    private T CheckedWrite<T>(CommandEventData eventData, T result) =>
        eventData.CommandSource is CommandSource.SaveChanges or CommandSource.ExecuteUpdate or CommandSource.ExecuteDelete or CommandSource.ExecuteSqlRaw
            ? Checked(result)
            : result;
}
