using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Events;

// The events a request raises while the content service holds its transaction open, kept until the outermost commit.
// Whatever rolls back, whether the transaction or a savepoint inside it, takes its events with it.
internal sealed class EventBuffer(EventDispatcher dispatcher, OutboxWriter outbox)
{
    private readonly List<CmsEvent> _events = [];

    public bool IsOpen { get; private set; }

    public int Mark => _events.Count;

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    // An event published twice is one event.
    public void Add(CmsEvent @event)
    {
        if (!_events.Exists(held => held.EventId == @event.EventId))
        {
            _events.Add(@event);
        }
    }

    public void Truncate(int mark) => _events.RemoveRange(mark, _events.Count - mark);

    public void Clear()
    {
        _events.Clear();
        outbox.Forget();
    }

    // Inside the transaction, so that the rows commit with the write.
    public ValueTask StageAsync(DbContext context, CancellationToken cancellationToken) => outbox.StageAsync(context, _events, cancellationToken);

    // After the commit. Nothing that goes wrong here may reach the caller, lose another event or skip the request for a drain.
    public async ValueTask DispatchAsync()
    {
        List<CmsEvent> events = [.. _events];
        _events.Clear();

        try
        {
            foreach (var @event in events)
            {
                await dispatcher.DispatchAsync(@event);
            }
        }
        finally
        {
            await outbox.KickIfStagedAsync();
        }
    }

    public async ValueTask DispatchNowAsync(CmsEvent @event)
    {
        try
        {
            await dispatcher.DispatchAsync(@event);
        }
        finally
        {
            await outbox.KickIfStagedAsync();
        }
    }
}
