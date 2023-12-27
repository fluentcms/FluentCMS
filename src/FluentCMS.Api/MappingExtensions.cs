using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Api;

/// <summary>
/// Extension methods for setting up mapping services in an <see cref="IServiceCollection" />.
/// </summary>
public static class MappingExtensions
{
    /// <summary>
    /// Adds AutoMapper mapping profiles to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add mapping services to.</param>
    /// <returns>The updated <see cref="IServiceCollection" /> with mapping services.</returns>
    /// <remarks>
    /// This method scans the current assembly for AutoMapper profiles and registers them.
    /// </remarks>
    public static IServiceCollection AddMappingProfiles(this IServiceCollection services)
    {
        // Add AutoMapper to the service collection, scanning for profiles in the current assembly
        services.AddAutoMapper(typeof(MappingExtensions).Assembly);

        return services;
    }
}
