using FluentCMS.Providers.Repositories.Abstractions;
using FluentCMS.Providers.Repositories.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace FluentCMS.Providers;

public static class ProviderFeatureBuilderExtensions
{
    public static ProviderFeatureBuilder AddProviders(this IServiceCollection services, Action<ProviderDiscoveryOptions>? configure = null)
    {
        services.PrepareProviderCatalogCache(configure);
        services.AddScoped<IProviderManager, ProviderManager>();
        return new ProviderFeatureBuilder(services);
    }

    public static ProviderFeatureBuilder UseConfiguration(this ProviderFeatureBuilder providerFeatureBuilder)
    {
        providerFeatureBuilder.Services.AddScoped<IProviderRepository, ConfigurationReadOnlyProviderRepository>();

        return providerFeatureBuilder;
    }

    private static void PrepareProviderCatalogCache(this IServiceCollection services, Action<ProviderDiscoveryOptions>? configure = null)
    {
        // Configure options
        var opts = new ProviderDiscoveryOptions();
        configure?.Invoke(opts);
        services.AddSingleton(opts);
        services.AddSingleton<ProviderCatalogCache>();

        var providerDiscovery = new ProviderDiscovery(opts);
        var providerModules = providerDiscovery.GetProviderModules();
        var providerModuleCatalogCache = new ProviderModuleCatalogCache(providerModules);
        services.AddSingleton(providerModuleCatalogCache);

        foreach (var module in providerModules)
        {
            try
            {
                module.ConfigureServices(services);
            }
            catch (Exception)
            {
                if (!opts.IgnoreExceptions)
                    throw;
            }
        }

        // for each interface type in the catalog, register a factory to resolve the default provider
        foreach (var (area, interfaceType) in providerModuleCatalogCache.GetRegisteredInterfaceTypes())
        {
            services.AddTransient(interfaceType, serviceProvider =>
            {
                var providerCatalogCache = serviceProvider.GetRequiredService<ProviderCatalogCache>();

                // Synchronous access to avoid deadlock issues
                var catalog = providerCatalogCache.GetActiveCatalog(area) ??
                    throw new InvalidOperationException($"No active provider found for area '{area}'. Ensure providers are properly initialized.");

                // Validate provider implements the requested interface
                if (!interfaceType.IsAssignableFrom(catalog.Module.ProviderType))
                {
                    throw new InvalidOperationException(
                        $"The active provider '{catalog.Name}' for area '{area}' does not implement the interface '{interfaceType.FullName}'. " +
                        $"Provider type: '{catalog.Module.ProviderType.FullName}'");
                }

                return CreateProviderInstance(serviceProvider, catalog);
            });
        }
    }

    private static object CreateProviderInstance(IServiceProvider serviceProvider, ProviderCatalog catalog)
    {
        var providerType = catalog.Module.ProviderType;

        // Validate constructor requirements
        var constructors = providerType.GetConstructors();
        if (constructors.Length == 0)
        {
            throw new InvalidOperationException(
                $"The provider type '{providerType.FullName}' has no public constructors.");
        }

        if (constructors.Length > 1)
        {
            throw new InvalidOperationException(
                $"The provider type '{providerType.FullName}' has multiple constructors. " +
                $"Only providers with a single constructor are supported.");
        }

        // Build constructor arguments
        var constructor = constructors[0];
        var constructorArgs = BuildConstructorArguments(constructor, catalog);

        try
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, providerType, constructorArgs);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create instance of provider '{providerType.FullName}' for area '{catalog.Module.Area}'. " +
                $"Provider: '{catalog.Name}'", ex);
        }
    }

    private static object[] BuildConstructorArguments(ConstructorInfo constructor, ProviderCatalog catalog)
    {
        if (catalog.Options == null || catalog.Module.OptionsType == null)
        {
            return [];
        }

        var argsList = new List<object>();
        foreach (var parameter in constructor.GetParameters())
        {
            // Direct options type injection
            if (parameter.ParameterType == catalog.Module.OptionsType)
            {
                argsList.Add(catalog.Options);
                continue;
            }

            // IOptions<T> wrapper injection
            if (parameter.ParameterType.IsGenericType &&
                parameter.ParameterType.GetGenericTypeDefinition() == typeof(IOptions<>) &&
                parameter.ParameterType.GetGenericArguments()[0] == catalog.Module.OptionsType)
            {
                var optionsWrapperType = typeof(OptionsWrapper<>).MakeGenericType(catalog.Module.OptionsType);
                var optionsWrapper = Activator.CreateInstance(optionsWrapperType, catalog.Options);
                argsList.Add(optionsWrapper!);
            }
        }

        return [.. argsList];
    }
}
