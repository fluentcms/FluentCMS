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

    public void Add(CmsEvent @event) => _events.Add(@event);

    public void Truncate(int mark) => _events.RemoveRange(mark, _events.Count - mark);

    public void Clear() => _events.Clear();

    // Inside the transaction, so that the rows commit with the write.
    public ValueTask StageAsync(DbContext context, CancellationToken cancellationToken) => outbox.StageAsync(context, _events, cancellationToken);

    // After the commit.
    public async ValueTask DispatchAsync()
    {
        List<CmsEvent> events = [.. _events];
        _events.Clear();

        foreach (var @event in events)
        {
            await dispatcher.DispatchAsync(@event);
        }

        await outbox.KickIfStagedAsync();
    }
}
