namespace FluentCMS.EventBus.InMemory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventPublisher(this IServiceCollection services, Action<EventPublisherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options with default or provided configuration
        services.Configure(configure ?? (_ => { }));

        // Register the generic event publisher
        services.TryAddSingleton<IEventPublisher, EventPublisher>();

        return services;
    }

    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IEvent
        where THandler : class, IEventSubscriber<TEvent>
    {
        // Register the handler with specified lifetime
        services.AddSingleton<IEventSubscriber<TEvent>, THandler>();

        return services;
    }

}
