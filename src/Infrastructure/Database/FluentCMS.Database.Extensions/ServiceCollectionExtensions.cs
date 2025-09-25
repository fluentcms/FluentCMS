using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Database.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers library-based multi-database support with high-performance, compile-time resolution.
    /// Each library marker can be mapped to a specific database provider.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configureMapping">Action to configure database mappings using fluent API.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddDatabaseManager(this IServiceCollection services, Action<ILibraryDatabaseMappingBuilder> configureMapping)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureMapping);

        // Create the library-based builder that will register services directly
        var builder = new LibraryDatabaseMappingBuilder(services);
        configureMapping(builder);

        // Validate that at least a default configuration is set
        if (!builder.HasDefaultConfiguration)
        {
            throw new InvalidOperationException(
                "A default database provider must be configured. Call SetDefault() and chain with a provider method (e.g., UseSqlServer()).");
        }

        // Register fallback factory for marker types without explicit mappings
        //builder.RegisterFallbackFactory();

        return services;
    }
}
