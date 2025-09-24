using FluentCMS.Database.Abstractions;

namespace FluentCMS.Database.Extensions;

/// <summary>
/// Fluent builder for configuring library-based database mappings with compile-time resolution.
/// Each library can be mapped to a specific database provider using marker interfaces.
/// </summary>
public interface ILibraryDatabaseMappingBuilder
{
    /// <summary>
    /// Configures the default database provider for libraries that don't have explicit mappings.
    /// Services using IDefaultLibraryMarker will automatically use this configuration.
    /// </summary>
    /// <returns>A library mapping builder to configure the default database provider.</returns>
    ILibraryMappingBuilder SetDefault();

    /// <summary>
    /// Maps a specific library marker to use a particular database provider.
    /// All services in the library that inject IDatabaseManager&lt;T&gt; with this marker will use the configured database.
    /// </summary>
    /// <typeparam name="TLibraryMarker">The library marker interface that inherits from IDatabaseManagerMarker.</typeparam>
    /// <returns>A library mapping builder to configure the database provider for this library.</returns>
    ILibraryMappingBuilder MapLibrary<TLibraryMarker>() where TLibraryMarker : IDatabaseManagerMarker;
}
