using FluentCMS.Providers.Storage;

namespace Microsoft.Extensions.DependencyInjection;

public static class FileStorageProviderExtensions
{
    public static IServiceCollection AddFileSystemStorageProvider(this IServiceCollection services)
    {
        services.AddScoped<IFileStorageProvider, FileSystemStorageProvider>();

        return services;
    }
}
