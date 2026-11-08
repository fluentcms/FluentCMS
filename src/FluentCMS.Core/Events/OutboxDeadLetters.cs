using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

internal sealed class OutboxDeadLetters(IServiceScopeFactory scopes, OutboxKick kick, TimeProvider time) : IEventDeadLetters
{
    public async Task<IReadOnlyList<FailedEventDelivery>> ListAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        return await context.Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.Status == OutboxStatus.Failed)
            .OrderByDescending(message => message.Sequence)
            .ThenBy(message => message.Id)
            .Skip(offset)
            .Take(limit)
            .Select(message => new FailedEventDelivery(message.Id, message.EventId, message.EventType, message.Handler, message.Attempts, message.LastError, message.OccurredAt))
            .ToListAsync(cancellationToken);
    }

    // The attempts start over, since a failed delivery has used all of them.
    public async Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        var queued = await context.Set<OutboxMessage>()
            .Where(message => message.Id == id && message.Status == OutboxStatus.Failed)
            .ExecuteUpdateAsync(
                message => message
                    .SetProperty(row => row.Status, OutboxStatus.Pending)
                    .SetProperty(row => row.Attempts, 0)
                    .SetProperty(row => row.AvailableAt, now),
                cancellationToken);

        if (queued == 1)
        {
            await kick.KickAsync();
        }

        return queued == 1;
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        return await context.Set<OutboxMessage>()
            .Where(message => message.Id == id && message.Status == OutboxStatus.Failed)
            .ExecuteDeleteAsync(cancellationToken) == 1;
    }
}
