namespace FluentCMS.DataSeeding;

/// <summary>
/// Registry for managing schema validator types used in data seeding operations.
/// </summary>
internal class SchemaValidatorRegistry
{
    /// <summary>
    /// Internal collection of registered validator types.
    /// </summary>
    private readonly HashSet<Type> _types = [];

    /// <summary>
    /// Gets a read-only collection of all registered validator types.
    /// </summary>
    public IReadOnlyCollection<Type> Types => _types;

    /// <summary>
    /// Adds a validator type to the registry.
    /// </summary>
    /// <param name="t">The validator type to add to the registry.</param>
    public void Add(Type t) => _types.Add(t);
}

