using FluentCMS.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Events;

// Runs the handlers of this process for the events of the job runner, after the commit that settled the job. The outbox rows
// were written in that commit by the job store, so only the handlers of this process are left. There is no request, so each
// event runs in a scope of its own, under a system operation on the site of the job. A job worker waits for them only as long
// as JobEventTimeout: handlers that take longer are left running, on a thread of their own, and the worker goes on.
internal sealed class BusJobEventSink(IServiceScopeFactory scopes, IOperationContextAccessor operation, OutboxOptions options, TimeProvider time, ILogger<BusJobEventSink> logger) : IJobEventSink
{
    public Task PublishAsync(JobFailedEvent failed) => DispatchAsync(failed);

    public Task PublishAsync(JobDeadLetteredEvent deadLettered) => DispatchAsync(deadLettered);

    private async Task DispatchAsync(CmsEvent @event)
    {
        try
        {
            await Task.Run(() => RunAsync(@event), CancellationToken.None).WaitAsync(options.JobEventTimeout, time);
        }
        catch (TimeoutException)
        {
            EventLog.DispatchAbandoned(logger, @event.GetType().Name, @event.EventId, options.JobEventTimeout);
        }
    }

    private async Task RunAsync(CmsEvent @event)
    {
        await using var scope = scopes.CreateAsyncScope();
        var previous = operation.Current;
        operation.Current = await EventOperation.CreateAsync(scope.ServiceProvider, @event.Source, @event.EventId, rowSite: null, time.GetUtcNow());
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
