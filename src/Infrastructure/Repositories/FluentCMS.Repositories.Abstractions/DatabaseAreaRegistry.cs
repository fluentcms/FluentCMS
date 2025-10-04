using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Registry that stores database area registrations for later configuration by DatabaseManager
/// </summary>
internal class DatabaseAreaRegistry
{
    private static readonly Dictionary<Type, DatabaseAreaRegistration> _registrations = [];

    /// <summary>
    /// Register a database area with its DbContext type and configuration
    /// </summary>
    public static void Register<TArea, TContext>(Action<DbContextOptionsBuilder>? contextConfiguration = null)
        where TArea : class, IDatabaseArea
        where TContext : DbContext
    {
        var areaType = typeof(TArea);
        var contextType = typeof(TContext);

        _registrations[areaType] = new DatabaseAreaRegistration
        {
            AreaType = areaType,
            ContextType = contextType,
            ContextConfiguration = contextConfiguration
        };
    }

    /// <summary>
    /// Get all registered database areas
    /// </summary>
    public static IReadOnlyDictionary<Type, DatabaseAreaRegistration> GetRegistrations()
    {
        return _registrations.AsReadOnly();
    }

    /// <summary>
    /// Get registration for a specific area type
    /// </summary>
    public static DatabaseAreaRegistration? GetRegistration<TArea>()
        where TArea : class, IDatabaseArea
    {
        return GetRegistration(typeof(TArea));
    }

    /// <summary>
    /// Get registration for a specific area type
    /// </summary>
    public static DatabaseAreaRegistration? GetRegistration(Type areaType)
    {
        _registrations.TryGetValue(areaType, out var registration);
        return registration;
    }

    /// <summary>
    /// Check if an area is registered
    /// </summary>
    public static bool IsRegistered<TArea>() where TArea : class, IDatabaseArea
    {
        return _registrations.ContainsKey(typeof(TArea));
    }

    /// <summary>
    /// Clear all registrations (mainly for testing)
    /// </summary>
    internal static void Clear()
    {
        _registrations.Clear();
    }
}

/// <summary>
/// Represents a database area registration with its associated DbContext
/// </summary>
internal class DatabaseAreaRegistration
{
    public required Type AreaType { get; init; }
    public required Type ContextType { get; init; }
    public Action<DbContextOptionsBuilder>? ContextConfiguration { get; init; }
}
