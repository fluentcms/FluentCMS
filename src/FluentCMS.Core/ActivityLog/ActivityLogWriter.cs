using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.ActivityLog;

// Writes on a context of its own, as the outbox does, so that it saves nothing the caller has tracked and drops nothing
// the caller has tracked. The row is the record of a write that has committed, so it is refused inside a transaction.
internal sealed class ActivityLogWriter(IServiceScopeFactory scopes, ITransactionProbe transaction, IOperationContextAccessor operation, TimeProvider time) : IActivityLog
{
    public ValueTask WriteAsync(ActivityEntry entry, CancellationToken cancellationToken) =>
        WriteAsync(entry, operation.Current is { } current ? EventSource.From(current) : EventSource.FromSystem(time.GetUtcNow()), cancellationToken);

    public ValueTask WriteAsync(ActivityEntry entry, EventSource source, CancellationToken cancellationToken)
    {
        if (transaction.InTransaction)
        {
            throw new InvalidOperationException(
                $"{ActivityErrorCodes.WriteInTransaction}: the activity log cannot be written inside a transaction, because the write it records may still roll back; write it after the transaction.");
        }

        return WriteAsync(entry, source, Guid.CreateVersion7(), cancellationToken);
    }

    // A row with this id is written once: a delivery that is repeated finds its row and leaves it.
    public async ValueTask WriteAsync(ActivityEntry entry, EventSource source, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(source);

        var row = new ActivityRow
        {
            Id = id,
            SiteId = source.SiteId == Guid.Empty ? null : source.SiteId,
            OccurredAt = source.OccurredAt.ToUniversalTime(),
            ActorType = source.ActorKind,
            ActorId = source.ActorId,
            ActorName = ActivityText.Required(source.ActorName, ActivityText.ActorNameMaxLength),
            Origin = source.Origin,
            ClientId = ActivityText.Of(source.ClientId, ActivityText.ClientIdMaxLength),
            Action = ActivityText.Action(entry.Action),
            TargetType = ActivityText.Of(entry.TargetType, ActivityText.TypeMaxLength),
            TargetId = ActivityText.Of(entry.TargetId, ActivityText.IdMaxLength),
            TargetLabel = ActivityText.Of(entry.TargetLabel, ActivityText.LabelMaxLength),
            Locale = ActivityText.Of(entry.Locale, ActivityText.LocaleMaxLength),
            Details = ActivityText.Details(entry.Details),
            CorrelationId = ActivityText.Required(source.CorrelationId, ActivityText.CorrelationIdMaxLength),
            Ip = ActivityText.Of(entry.Ip, ActivityText.IpMaxLength),
        };

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        if (await context.Set<ActivityRow>().AnyAsync(existing => existing.Id == id, cancellationToken))
        {
            return;
        }

        context.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same delivery ran on another instance at the same time, or the write really failed.
            context.ChangeTracker.Clear();
            if (!await context.Set<ActivityRow>().AnyAsync(existing => existing.Id == id, cancellationToken))
            {
                throw;
            }
        }
    }
}
