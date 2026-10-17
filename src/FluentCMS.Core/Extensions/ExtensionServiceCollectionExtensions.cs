using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentCMS.Core.Extensions;

public static class ExtensionServiceCollectionExtensions
{
    public static IServiceCollection AddFluentCmsExtension<TContract, TImplementation>(this IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        return services.AddFluentCmsExtension(typeof(TContract), typeof(TImplementation));
    }

    public static IServiceCollection AddFluentCmsExtension(this IServiceCollection services, Type contract, Type implementation)
    {
        if (!IsExtensionPoint(contract))
        {
            throw new ArgumentException($"{contract} is not marked with [ExtensionPoint].", nameof(contract));
        }

        if (!implementation.IsClass || implementation.IsAbstract || !contract.IsAssignableFrom(implementation))
        {
            throw new ArgumentException($"{implementation} must be a concrete class implementing {contract}.", nameof(implementation));
        }

        services.TryAddSingleton<IExtensionCatalog, ExtensionCatalog>();

        // Explicit and discovered registrations converge here, so a built-in and a plugin look the same to the catalog.
        if (services.Any(descriptor =>
                !descriptor.IsKeyedService
                && descriptor.ServiceType == contract
                && descriptor.ImplementationType == implementation))
        {
            return services;
        }

        services.AddSingleton(contract, implementation);
        services.AddSingleton(new ExtensionDescriptor(contract, implementation));

        return services;
    }

    public static IServiceCollection AddFluentCmsExtensions(this IServiceCollection services, Assembly assembly)
    {
        services.TryAddSingleton<IExtensionCatalog, ExtensionCatalog>();

        var implementations = assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.IsGenericTypeDefinition);

        foreach (var implementation in implementations)
        {
            foreach (var contract in implementation.GetInterfaces().Where(IsExtensionPoint))
            {
                services.AddFluentCmsExtension(contract, implementation);
            }
        }

        return services;
    }

    private static bool IsExtensionPoint(Type type)
    {
        return type.IsInterface && type.IsDefined(typeof(ExtensionPointAttribute), inherit: false);
    }
}
