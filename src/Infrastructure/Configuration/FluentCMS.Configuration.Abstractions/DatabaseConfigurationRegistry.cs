using System.Collections.Concurrent;

// Namespace for configuration abstractions
namespace FluentCMS.Configuration.Abstractions;

// Registry class to track configuration sections that should be stored in database
// This class should be registered as a singleton in the DI container
public class DatabaseConfigurationRegistry
{
    // Thread-safe dictionary to store registered sections with their types
    // Uses case-insensitive comparison for section names to match IConfiguration behavior
    private readonly ConcurrentDictionary<string, Type> _registeredSections = new(StringComparer.OrdinalIgnoreCase);

    // Registers a configuration section with its options type
    // Validates that the type is suitable for use as an options class
    // Throws InvalidOperationException if section already registered with different type
    // Allows re-registration with same type (idempotent operation)
    public void RegisterSection(string sectionName, Type optionsType)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            throw new ArgumentException("Section name cannot be null or whitespace", nameof(sectionName));

        ArgumentNullException.ThrowIfNull(optionsType);

        // Validate that the type is suitable for use as an options class
        ValidateOptionsType(optionsType);

        // Attempt to add or update the registration
        // If section exists with different type, throw exception
        var added = _registeredSections.AddOrUpdate(
            sectionName,
            optionsType,
            (key, existingType) =>
            {
                // Allow re-registration with same type (idempotent)
                if (existingType == optionsType)
                    return existingType;

                // Different type - this is a configuration error
                throw new InvalidOperationException(
                    $"Section '{sectionName}' is already registered with type '{existingType.FullName}'. " +
                    $"Cannot register with different type '{optionsType.FullName}'.");
            });
    }

    // Validates that a type is suitable for use as an options class
    // Options classes must be non-abstract reference types with a parameterless constructor
    private static void ValidateOptionsType(Type optionsType)
    {
        // Must be a class (reference type)
        if (!optionsType.IsClass)
        {
            throw new ArgumentException(
                $"Options type '{optionsType.FullName}' must be a class. " +
                $"Interfaces, structs, and other value types are not supported.",
                nameof(optionsType));
        }

        // Must not be abstract
        if (optionsType.IsAbstract)
        {
            throw new ArgumentException(
                $"Options type '{optionsType.FullName}' cannot be abstract. " +
                $"The Options pattern requires concrete types that can be instantiated.",
                nameof(optionsType));
        }

        // Must not be a generic type definition (open generic)
        if (optionsType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Options type '{optionsType.FullName}' cannot be an open generic type. " +
                $"Provide a closed generic type with all type parameters specified.",
                nameof(optionsType));
        }

        // Verify parameterless constructor exists (required by Options pattern)
        // This includes both explicit and implicit parameterless constructors
        var hasParameterlessConstructor = optionsType.GetConstructor(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null) != null;

        if (!hasParameterlessConstructor)
        {
            throw new ArgumentException(
                $"Options type '{optionsType.FullName}' must have a public parameterless constructor. " +
                $"This is required by the Options pattern for instantiation.",
                nameof(optionsType));
        }
    }

    // Retrieves read-only dictionary of all registered sections
    public IReadOnlyDictionary<string, Type> GetRegisteredSections()
    {
        return _registeredSections;
    }

    // Checks if a section is registered
    public bool IsRegistered(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            return false;

        return _registeredSections.ContainsKey(sectionName);
    }

    // Clears all registered sections (useful for testing or reconfiguration)
    public void Clear()
    {
        _registeredSections.Clear();
    }
}
