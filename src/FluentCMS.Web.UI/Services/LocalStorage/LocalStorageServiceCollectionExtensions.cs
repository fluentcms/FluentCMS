using FluentCMS.Web.UI.Services.LocalStorage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

[ExcludeFromCodeCoverage]
public static class LocalStorageServiceCollectionExtensions
{
    public static IServiceCollection AddLocalStorage(this IServiceCollection services)
        => AddLocalStorage(services, null);

    public static IServiceCollection AddLocalStorage(this IServiceCollection services, Action<LocalStorageOptions>? configure)
    {
        services.TryAddScoped<IJsonSerializer, SystemTextJsonSerializer>();
        services.TryAddScoped<IStorageProvider, BrowserStorageProvider>();
        services.TryAddScoped<ILocalStorageService, LocalStorageService>();
        services.TryAddScoped<ISyncLocalStorageService, LocalStorageService>();
        if (services.All(serviceDescriptor => serviceDescriptor.ServiceType != typeof(IConfigureOptions<LocalStorageOptions>)))
        {
            services.Configure<LocalStorageOptions>(configureOptions =>
            {
                configure?.Invoke(configureOptions);
            });
        }

        return services;
    }
}
