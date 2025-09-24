namespace FluentCMS.DataSeeding;

/// <summary>
/// Registry for managing data seeder types.
/// Maintains a collection of types that implement data seeding functionality.
/// </summary>
internal class DataSeederRegistry
{
    /// <summary>
    /// Internal collection of registered seeder types.
    /// </summary>
    private readonly HashSet<Type> _types = [];

    /// <summary>
    /// Gets a read-only collection of all registered seeder types.
    /// </summary>
    public IReadOnlyCollection<Type> Types => _types;

    /// <summary>
    /// Adds a seeder type to the registry.
    /// </summary>
    /// <param name="t">The type to add to the registry.</param>
    public void Add(Type t) => _types.Add(t);
}