namespace FluentCMS.Infrastructure.EventBus;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IEvent
        where THandler : class, IEventSubscriber<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the handler with specified lifetime
        services.AddScoped<IEventSubscriber<TEvent>, THandler>();

        return services;
    }
}
