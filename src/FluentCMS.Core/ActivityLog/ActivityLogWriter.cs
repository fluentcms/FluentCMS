using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.ActivityLog;

// Writes on the context of its own scope, which is not the context of the caller's transaction: the row is the record of
// a write that has committed.
internal sealed class ActivityLogWriter(SystemDbContext context, IOperationContextAccessor operation, TimeProvider time) : IActivityLog
{
    public ValueTask WriteAsync(ActivityEntry entry, CancellationToken cancellationToken) =>
        WriteAsync(entry, operation.Current is { } current ? EventSource.From(current) : EventSource.FromSystem(time.GetUtcNow()), cancellationToken);

    public ValueTask WriteAsync(ActivityEntry entry, EventSource source, CancellationToken cancellationToken) =>
        WriteAsync(entry, source, Guid.CreateVersion7(), cancellationToken);

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
