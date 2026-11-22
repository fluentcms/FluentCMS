using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Core.IntegrationTests.Identity;

// Fails every save that would write the outbox row of an end of sessions, in either pool, as a database that refuses the
// write would (#3531).
internal sealed class SessionRowFailure : SaveChangesInterceptor
{
    private static readonly string[] Ended = [typeof(UserSessionsEndedEvent).FullName!, typeof(MemberSessionsEndedEvent).FullName!];

    private int _failed;

    public int Failed => Volatile.Read(ref _failed);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added && Ended.Contains(entry.Entity.EventType)))
        {
            Interlocked.Increment(ref _failed);
            throw new DbUpdateException("The save fails for the test.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
