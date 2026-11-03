using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;

namespace FluentCMS.Core.Events;

// Turns events into the rows of the outbox handlers registered for them, in a sequence that rises with the order they are
// built in, so that the events of one commit are delivered in the order they were raised.
internal sealed class OutboxRows(OutboxRegistry registry, TimeProvider time)
{
    private long _last;

    public List<OutboxMessage> Build(IEnumerable<CmsEvent> events)
    {
        var now = time.GetUtcNow();
        List<OutboxMessage> rows = [];
        HashSet<(Guid, string)> seen = [];
        foreach (var @event in events)
        {
            var type = @event.GetType();
            var handlers = registry.ForEvent(type);
            if (handlers.Count == 0)
            {
                continue;
            }

            var payload = JsonSerializer.Serialize(@event, type, EventJson.Options);
            foreach (var handler in handlers)
            {
                if (!seen.Add((@event.EventId, handler.Name)))
                {
                    continue;
                }

                rows.Add(new OutboxMessage
                {
                    EventId = @event.EventId,
                    EventType = type.FullName ?? type.Name,
                    Handler = handler.Name,
                    Payload = payload,
                    SiteId = @event.Source.SiteId == Guid.Empty ? null : @event.Source.SiteId,
                    OccurredAt = @event.Source.OccurredAt.ToUniversalTime(),
                    Sequence = NextSequence(now),
                    AvailableAt = now,
                });
            }
        }

        return rows;
    }

    // The same text always gives the same id, so that an event built again after a retry is the same event.
    public static Guid StableId(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));

    private long NextSequence(DateTimeOffset now)
    {
        while (true)
        {
            var last = Interlocked.Read(ref _last);
            var next = Math.Max(last + 1, now.UtcTicks);
            if (Interlocked.CompareExchange(ref _last, next, last) == last)
            {
                return next;
            }
        }
    }
}
