namespace Microsoft.Extensions.DependencyInjection.Extensions;

public static class DecoratorServiceCollectionExtensions
{
    public static IServiceCollection Decorate<TService, TDecorator>(this IServiceCollection services)
        where TDecorator : class, TService
    {
        var originalServiceDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TService));
        if (originalServiceDescriptor == null)
        {
            throw new InvalidOperationException($"Service type {typeof(TService).Name} has not been registered.");
        }

        services.Replace(ServiceDescriptor.Describe(
            typeof(TService),
            serviceProvider =>
            {
                var originalService = serviceProvider.GetService(originalServiceDescriptor.ImplementationType);
                var parameters = new List<object> { originalService };

                // Resolve additional dependencies from the service provider
                var constructorInfo = typeof(TDecorator).GetConstructors().First();
                foreach (var parameter in constructorInfo.GetParameters())
                {
                    if (parameter.ParameterType != typeof(TService))
                    {
                        parameters.Add(serviceProvider.GetRequiredService(parameter.ParameterType));
                    }
                }

                return Activator.CreateInstance(typeof(TDecorator), parameters.ToArray());
            },
            originalServiceDescriptor.Lifetime));

        return services;
    }
}

