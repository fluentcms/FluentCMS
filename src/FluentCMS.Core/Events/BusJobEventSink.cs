using FluentCMS.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Events;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

// Runs the handlers of this process for the events of the job runner, after the commit that settled the job. The outbox rows
// were written in that commit by the job store, so only the handlers of this process are left. There is no request, so each
// event runs in a scope of its own under a system operation.
internal sealed class BusJobEventSink(IServiceScopeFactory scopes, IOperationContextAccessor operation, TimeProvider time) : IJobEventSink
{
    public Task PublishAsync(JobFailedEvent failed) => DispatchAsync(failed);

    public Task PublishAsync(JobDeadLetteredEvent deadLettered) => DispatchAsync(deadLettered);

    private async Task DispatchAsync(CmsEvent @event)
    {
        await using var scope = scopes.CreateAsyncScope();
        var previous = operation.Current;
        operation.Current = new OperationContext(Principal.System, Origin.Job, new SiteId(DefaultSite.Id), DefaultSite.Locale, ClientId: null, @event.EventId.ToString(), time.GetUtcNow());
        try
        {
            await scope.ServiceProvider.GetRequiredService<EventDispatcher>().DispatchAsync(@event);
        }
        finally
        {
            operation.Current = previous;
        }
    }
}
