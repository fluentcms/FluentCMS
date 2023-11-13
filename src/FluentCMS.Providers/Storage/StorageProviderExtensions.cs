using FluentCMS.Providers.Storage;

namespace Microsoft.Extensions.DependencyInjection;

public static class StorageProviderExtensions
{
    public static IServiceCollection AddFileSystemStorageProvider(this IServiceCollection services)
    {
        services.AddScoped<IStorageProvider, FileSystemStorageProvider>();

        return services;
    }
}
