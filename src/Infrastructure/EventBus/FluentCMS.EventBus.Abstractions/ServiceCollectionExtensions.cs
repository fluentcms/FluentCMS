using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.EventBus.Abstractions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IEvent
        where THandler : class, IEventSubscriber<TEvent>
    {
        // Register the handler with specified lifetime
        services.AddTransient<IEventSubscriber<TEvent>, THandler>();

        return services;
    }
}
