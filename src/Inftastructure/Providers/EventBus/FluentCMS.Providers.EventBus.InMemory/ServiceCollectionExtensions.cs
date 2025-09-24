namespace FluentCMS.Providers.EventBus.InMemory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the generic event publisher
        services.TryAddScoped<IEventPublisher, EventPublisher>();

        return services;
    }
}
