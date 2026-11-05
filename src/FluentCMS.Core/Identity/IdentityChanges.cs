using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Core.Identity;

// One transaction for a change to users or invitations, and the events it raises: their outbox rows commit with the change,
// and in-process handlers hear them after the commit. A change inside another joins it, so accepting an invitation is one
// unit with the user it creates. A failed IdentityResult rolls back like an exception.
internal sealed class IdentityChanges(SystemDbContext context, EventBuffer buffer, IEventBus bus, IOperationContextAccessor accessor, TimeProvider time)
{
    private int _depth;

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (_depth > 0)
        {
            return await work();
        }

        if (context.Database.CurrentTransaction is not null || buffer.IsOpen)
        {
            throw new InvalidOperationException($"{IdentityErrorCodes.WriteInTransaction}: users and invitations are written in a transaction of their own, so not inside another.");
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

    // Users are instance-wide, so their events and activity rows belong to no site.
    public ValueTask RaiseAsync(CmsEvent message, CancellationToken cancellationToken)
    {
        var source = accessor.Current is { } operation
            ? EventSource.From(operation) with { SiteId = Guid.Empty }
            : EventSource.FromSystem(time.GetUtcNow());

        return bus.PublishAsync(message with { Source = source }, cancellationToken);
    }

    // What the rolled back saves left in the context is not in the database.
    private void Discard()
    {
        buffer.Clear();
        context.ChangeTracker.Clear();
    }
}
