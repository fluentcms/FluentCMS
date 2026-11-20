using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Providers;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity;

// One transaction for a change to users, invitations or API keys, and the events it raises: their outbox rows commit with
// the change, and in-process handlers hear them after the commit. A change inside another joins it, so accepting an invitation is one
// unit with the user it creates. A failed IdentityResult rolls back like an exception.
internal sealed class IdentityChanges(SystemDbContext context, EventBuffer buffer, IEventBus bus, IOperationContextAccessor accessor, TimeProvider time, IDistributedLock locks)
{
    public const string NamesLock = "fluentcms:user-names";

    // Long enough for a holder that stopped without releasing the lock to lose its lease, as for SuperAdminGuard.
    private static readonly TimeSpan NamesWait = TimeSpan.FromSeconds(45);

    private int _depth;
    private bool _names;

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (_depth > 0)
        {
            return await work();
        }

        if (context.Database.CurrentTransaction is not null || buffer.IsOpen)
        {
            throw new InvalidOperationException($"{IdentityErrorCodes.WriteInTransaction}: users, invitations and API keys are written in a transaction of their own, so not inside another.");
        }

        T result;
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        _depth++;
        buffer.Open();
        await using (transaction)
        {
            try
            {
                result = await work();
                if (result is IdentityResult { Succeeded: false })
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    Discard();
                    return result;
                }

                await buffer.StageAsync(context, cancellationToken);
                await transaction.CommitAsync(CancellationToken.None);
            }
            catch
            {
                Discard();
                throw;
            }
            finally
            {
                _depth--;
                buffer.Close();
            }
        }

        await buffer.DispatchAsync(buffer.Take());
        return result;
    }

    // A change that gives an admin user a name or an address. No key of the database keeps one user's name from being
    // another's address, so these changes run one at a time under a lock, on any provider or instance, and CmsUserStore
    // checks the pair again inside each: of two at once the second sees what the first committed (#3373). The lock is taken
    // before the transaction opens, as SuperAdminGuard's is, so a change that creates or renames a user inside a
    // transaction of its own starts it here.
    public async Task<T> RunNamingAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (_names)
        {
            return await RunAsync(work, cancellationToken);
        }

        if (_depth > 0 || context.Database.CurrentTransaction is not null || buffer.IsOpen)
        {
            throw new InvalidOperationException($"{IdentityErrorCodes.WriteInTransaction}: a change that names an admin user takes the lock {NamesLock} before its transaction opens, so not inside another.");
        }

        await using var held = await locks.TryAcquireAsync(NamesLock, NamesWait, cancellationToken)
            ?? throw new TimeoutException($"The lock {NamesLock} was not free within {NamesWait.TotalSeconds:0} seconds, so the change was not made; try again.");
        _names = true;
        try
        {
            return await RunAsync(work, cancellationToken);
        }
        finally
        {
            _names = false;
        }
    }

    // Users and keys are instance-wide, so their events and activity rows belong to no site.
    public ValueTask RaiseAsync(CmsEvent message, CancellationToken cancellationToken)
    {
        var source = accessor.Current is { } operation
            ? EventSource.From(operation) with { SiteId = Guid.Empty }
            : EventSource.FromSystem(time.GetUtcNow());

        return bus.PublishAsync(message with { Source = source }, cancellationToken);
    }

    // An event that is the whole change, such as a sign-in, with the source the caller gave it: its outbox row is the only
    // write of its transaction.
    public Task RecordAsync(CmsEvent message, CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                await bus.PublishAsync(message, cancellationToken);
                return true;
            },
            cancellationToken);

    // What the rolled back saves left in the context is not in the database.
    private void Discard()
    {
        buffer.Clear();
        context.ChangeTracker.Clear();
    }
}
