using FluentCMS.Repositories.LiteDb;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class LiteDbServiceExtensions
{
    public static IServiceCollection AddLiteDbRepositories(this IServiceCollection services, string connectionString)
    {
        // Register LiteDB context and options
        services.AddSingleton(provider => CreateLiteDBOptions(provider, connectionString));
        services.AddSingleton<ILiteDBContext, LiteDBContext>();

        // Register all repositories using reflection
        RegisterRepositories(services);

        return services;
    }

    private static LiteDBOptions<LiteDBContext> CreateLiteDBOptions(IServiceProvider provider, string connectionString)
    {
        var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");
        var connString = configuration.GetConnectionString(connectionString);
        return connString is not null
            ? new LiteDBOptions<LiteDBContext>(connString)
            : throw new InvalidOperationException($"Connection string '{connectionString}' not found.");
    }

    private static void RegisterRepositories(IServiceCollection services)
    {
        var repositoryTypes = typeof(LiteDbServiceExtensions).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Repository"))
            .ToList();

        foreach (var repositoryType in repositoryTypes)
        {
            var interfaceType = repositoryType.GetInterfaces().FirstOrDefault(i => i.Name.EndsWith(repositoryType.Name))
                ?? throw new InvalidOperationException($"Interface for repository '{repositoryType.Name}' not found.");

            services.AddScoped(interfaceType, repositoryType);
        }
    }
}
