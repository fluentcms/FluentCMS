namespace Microsoft.Extensions.DependencyInjection;

public static class MessageBusServiceExtensions
{
    public static IServiceCollection AddInMemoryMessageBus(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ServiceExtensions).Assembly));
        services.AddScoped<IMessagePublisher, InMemoryMessagePublisher>();

        return services;
    }
}
