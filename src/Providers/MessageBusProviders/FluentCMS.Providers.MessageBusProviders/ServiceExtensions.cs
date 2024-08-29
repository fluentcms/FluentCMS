using FluentCMS.Providers.MessageBusProviders;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddInMemoryMessageBusProvider(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ServiceExtensions).Assembly));
        services.AddScoped<IMessagePublisher, InMemoryMessagePublisher>();

        return services;
    }
}
