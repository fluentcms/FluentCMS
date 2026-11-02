using System.Text.Json;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

// Records events for the outbox handlers that want them. Staging joins the transaction of the caller's context, so
// that the rows commit or roll back with the write that raised the events; recording writes on a context of its own.
internal sealed class OutboxWriter(IServiceScopeFactory scopes, OutboxRegistry registry, OutboxKick kick, TimeProvider time)
{
    public const int HandlerMaxLength = 128;

    private bool _staged;

    public async ValueTask StageAsync(DbContext context, IEnumerable<CmsEvent> events, CancellationToken cancellationToken)
    {
        var rows = RowsOf(events);
        if (rows.Count == 0)
        {
            return;
        }

        context.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        _staged = true;
    }

    public async ValueTask RecordAsync(IEnumerable<CmsEvent> events, CancellationToken cancellationToken)
    {
        var rows = RowsOf(events);
        if (rows.Count == 0)
        {
            return;
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var own = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            own.AddRange(rows);
            await own.SaveChangesAsync(cancellationToken);
        }

        _staged = true;
    }

    // After the commit: asks for the rows to be delivered at once. A failure waits for the scheduled drain.
    public async ValueTask KickIfStagedAsync()
    {
        if (!_staged)
        {
            return;
        }

        _staged = false;
        await kick.KickAsync();
    }

    private List<OutboxMessage> RowsOf(IEnumerable<CmsEvent> events)
    {
        var now = time.GetUtcNow();
        List<OutboxMessage> rows = [];
        foreach (var @event in events)
        {
            var type = @event.GetType();
            var handlers = registry.ForEvent(type);
            if (handlers.Count == 0)
            {
                continue;
            }

            var payload = JsonSerializer.Serialize(@event, type, EventJson.Options);
            for (var position = 0; position < handlers.Count; position++)
            {
                rows.Add(new OutboxMessage
                {
                    EventId = @event.EventId,
                    EventType = type.FullName ?? type.Name,
                    Handler = handlers[position].Name,
                    Payload = payload,
                    SiteId = @event.Source.SiteId == Guid.Empty ? null : @event.Source.SiteId,
                    OccurredAt = @event.Source.OccurredAt,
                    Position = position,
                    AvailableAt = now,
                });
            }
        }

        return rows;
    }
}
