using FluentCMS.Providers.FileStorageProviders;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddLocalFileStorageProvider(this IServiceCollection services)
    {
        services.AddScoped<IFileStorageProvider, LocalFileStorageProvider>();
        services.AddOptions<FileStorageConfig>()
            .BindConfiguration("Providers:FileStorage:FileStorageConfig");
        return services;
    }
}
