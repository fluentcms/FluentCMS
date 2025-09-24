using FluentCMS.Database.Abstractions;

namespace FluentCMS.Database.Extensions;

/// <summary>
/// Fluent builder for configuring database provider for a specific library mapping.
/// Extension methods from database provider libraries will extend this interface.
/// </summary>
public interface ILibraryMappingBuilder
{
    /// <summary>
    /// Registers a database provider for this library mapping.
    /// This method is used internally by extension methods from database provider libraries.
    /// </summary>
    /// <param name="providerName">The unique name of the database provider.</param>
    /// <param name="connectionString">The connection string for this mapping.</param>
    /// <param name="factory">Factory function to create the database manager instance.</param>
    /// <returns>The builder instance for method chaining.</returns>
    ILibraryMappingBuilder RegisterProvider(string providerName, string connectionString, Func<string, IServiceProvider, IDatabaseManager> factory);
}
