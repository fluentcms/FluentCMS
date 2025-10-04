using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Base class for database provider extension methods.
/// Provider packages should create extension methods that use these base methods.
/// </summary>
public static class DatabaseProviderExtensions
{
    /// <summary>
    /// Register a database provider for area-specific configuration.
    /// This method is used by provider packages to extend the fluent API.
    /// </summary>
    /// <typeparam name="TArea">The database area marker interface</typeparam>
    /// <param name="configuration">The area configuration</param>
    /// <param name="provider">The database provider</param>
    /// <param name="connectionString">Connection string</param>
    /// <param name="providerOptions">Provider-specific options</param>
    /// <returns>The configuration for chaining</returns>
    public static DatabaseAreaConfiguration<TArea> UseProvider<TArea>(this DatabaseAreaConfiguration<TArea> configuration, IDatabaseProvider provider, string connectionString, Action<DbContextOptionsBuilder>? providerOptions = null)
        where TArea : class, IDatabaseArea
    {
        return configuration.UseProvider(provider, connectionString, providerOptions);
    }

    /// <summary>
    /// Register a database provider for default configuration.
    /// This method is used by provider packages to extend the fluent API.
    /// </summary>
    /// <param name="configuration">The default configuration</param>
    /// <param name="provider">The database provider</param>
    /// <param name="connectionString">Connection string</param>
    /// <param name="providerOptions">Provider-specific options</param>
    /// <returns>The configuration for chaining</returns>
    public static DefaultDatabaseConfiguration UseProvider(this DefaultDatabaseConfiguration configuration, IDatabaseProvider provider, string connectionString, Action<DbContextOptionsBuilder>? providerOptions = null)
    {
        return configuration.UseProvider(provider, connectionString, providerOptions);
    }
}

/// <summary>
/// Registry for database providers. Provider packages should register their providers here.
/// </summary>
public static class DatabaseProviderRegistry
{
    private static readonly Dictionary<string, IDatabaseProvider> _providers = [];

    /// <summary>
    /// Register a database provider by name
    /// </summary>
    /// <param name="providerName">Unique provider name (e.g., "Sqlite", "SqlServer")</param>
    /// <param name="provider">The provider implementation</param>
    public static void RegisterProvider(string providerName, IDatabaseProvider provider)
    {
        _providers[providerName] = provider;
    }

    /// <summary>
    /// Get a registered provider by name
    /// </summary>
    /// <param name="providerName">The provider name</param>
    /// <returns>The provider implementation, or null if not found</returns>
    public static IDatabaseProvider? GetProvider(string providerName)
    {
        _providers.TryGetValue(providerName, out var provider);
        return provider;
    }

    /// <summary>
    /// Get all registered provider names
    /// </summary>
    /// <returns>Collection of provider names</returns>
    public static IReadOnlyCollection<string> GetRegisteredProviderNames()
    {
        return _providers.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Check if a provider is registered
    /// </summary>
    /// <param name="providerName">The provider name</param>
    /// <returns>True if registered, false otherwise</returns>
    public static bool IsProviderRegistered(string providerName)
    {
        return _providers.ContainsKey(providerName);
    }

    /// <summary>
    /// Clear all registered providers (mainly for testing)
    /// </summary>
    internal static void Clear()
    {
        _providers.Clear();
    }
}
