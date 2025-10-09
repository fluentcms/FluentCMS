namespace FluentCMS.Infrastructure.EventBus.InMemory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryEventBus(this IServiceCollection services, Action<EventPublisherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options with default or provided configuration
        services.Configure(configure ?? (_ => { }));

        // Register the generic event publisher
        services.TryAddSingleton<IEventPublisher, EventPublisher>();

        return services;
    }
}
