using FluentCMS.Database.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Database.Extensions;

internal sealed class LibraryDatabaseMappingBuilder(IServiceCollection services) : ILibraryDatabaseMappingBuilder, ILibraryMappingBuilder
{
    private readonly IServiceCollection services = services ?? throw new ArgumentNullException(nameof(services));
    private Type? _currentMarkerType;
    private bool _isDefault;
    private bool _hasDefaultConfiguration;

    /// <summary>
    /// Gets whether a default configuration has been set.
    /// </summary>
    public bool HasDefaultConfiguration => _hasDefaultConfiguration;


    /// <summary>
    /// Configures the default database provider for libraries that don't have explicit mappings.
    /// </summary>
    public ILibraryMappingBuilder SetDefault()
    {
        if (_hasDefaultConfiguration)
            throw new InvalidOperationException("Default database manager has already been configured.");

        _isDefault = true;
        _currentMarkerType = typeof(IDatabaseManagerMarker);
        return this;
    }

    /// <summary>
    /// Maps a specific library marker to use a particular database provider.
    /// </summary>
    public ILibraryMappingBuilder MapLibrary<TLibraryMarker>() where TLibraryMarker : IDatabaseManagerMarker
    {
        _isDefault = false;
        _currentMarkerType = typeof(TLibraryMarker);
        return this;
    }

    /// <summary>
    /// Registers a database provider for the current library mapping.
    /// </summary>
    public ILibraryMappingBuilder RegisterProvider(string providerName, string connectionString, Func<string, IServiceProvider, IDatabaseManager> factory)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name cannot be null or empty.", nameof(providerName));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

        ArgumentNullException.ThrowIfNull(factory);

        if (_currentMarkerType == null)
            throw new InvalidOperationException("No library marker specified for mapping. Call SetDefault() or MapLibrary() first.");

        if (_isDefault)
        {
            _hasDefaultConfiguration = true;
            services.AddScoped(sp =>
            {
                return factory(connectionString, sp); // returns an IDatabaseManager
            });

            // DEFAULT: map open generic to wrapper (will inject the *unkeyed* IDatabaseManager)
            services.AddScoped(typeof(IDatabaseManager<>), typeof(TypedDatabaseManager<>));
        }
        else
        {
            // Close the generic service and implementation
            var serviceType = typeof(IDatabaseManager<>).MakeGenericType(_currentMarkerType);
            var implType = typeof(TypedDatabaseManager<>).MakeGenericType(_currentMarkerType);

            // Closed registration overrides the open generic one.
            services.AddScoped(serviceType, sp =>
            {
                // Get the inner non-generic IDatabaseManager
                var inner = factory(connectionString, sp);
                // Pass the keyed inner into the wrapper
                return ActivatorUtilities.CreateInstance(sp, implType, inner);
            });

        }

        return this;
    }
}
