using FluentCMS.Core.Jobs;
using FluentCMS.Events;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

// The job runner has no request, so each event is raised in a scope of its own.
internal sealed class BusJobEventSink(IServiceScopeFactory scopes, TimeProvider time) : IJobEventSink
{
    public Task PublishAsync(JobFailedEvent failed) => RaiseAsync(failed with { Source = EventSource.FromSystem(time.GetUtcNow()) });

    public Task PublishAsync(JobDeadLetteredEvent deadLettered) => RaiseAsync(deadLettered with { Source = EventSource.FromSystem(time.GetUtcNow()) });

    private async Task RaiseAsync<TEvent>(TEvent @event)
        where TEvent : CmsEvent
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(@event);
    }
}
