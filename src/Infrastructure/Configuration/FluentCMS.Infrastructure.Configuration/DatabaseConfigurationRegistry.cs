// Namespace for configuration abstractions
namespace FluentCMS.Infrastructure.Configuration;

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

        // Validate the section name for security and compatibility
        ValidateSectionName(sectionName);

        // Validate that the type is suitable for use as an options class
        ValidateOptionsType(optionsType);

        // Attempt to add or update the registration
        // If section exists with different type, throw exception
        // Return value is intentionally not used as AddOrUpdate handles both add and update cases
        _registeredSections.AddOrUpdate(
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

    // Validates section name for security and compatibility
    // Ensures section names follow best practices and avoid potential issues
    private static void ValidateSectionName(string sectionName)
    {
        // Check maximum length to prevent potential issues with storage systems
        const int maxSectionNameLength = 255;
        if (sectionName.Length > maxSectionNameLength)
        {
            throw new ArgumentException(
                $"Section name '{sectionName}' exceeds maximum length of {maxSectionNameLength} characters.",
                nameof(sectionName));
        }

        // Check for invalid characters that could cause issues with configuration systems
        // Configuration section names should be simple identifiers or colon-separated paths
        var invalidChars = new[] { '\\', '/', '*', '?', '"', '<', '>', '|', '\0' };
        if (sectionName.IndexOfAny(invalidChars) >= 0)
        {
            throw new ArgumentException(
                $"Section name '{sectionName}' contains invalid characters. " +
                $"Avoid characters: \\ / * ? \" < > | and null characters.",
                nameof(sectionName));
        }

        // Warn against leading/trailing colons which indicate malformed paths
        if (sectionName.StartsWith(':') || sectionName.EndsWith(':'))
        {
            throw new ArgumentException(
                $"Section name '{sectionName}' cannot start or end with a colon. " +
                $"Use colons only to separate nested section paths (e.g., 'Parent:Child').",
                nameof(sectionName));
        }

        // Check for consecutive colons which indicate empty section names
        if (sectionName.Contains("::"))
        {
            throw new ArgumentException(
                $"Section name '{sectionName}' contains consecutive colons, which creates empty section segments.",
                nameof(sectionName));
        }
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

    // Retrieves a defensive copy of all registered sections as a read-only dictionary
    // Returns a snapshot of current registrations to prevent external modification
    // Thread-safe: Returns a point-in-time snapshot of the registry
    public IReadOnlyDictionary<string, Type> GetRegisteredSections()
    {
        // Create a defensive copy to prevent callers from casting back to ConcurrentDictionary
        // This ensures encapsulation and prevents external modification of internal state
        return new Dictionary<string, Type>(_registeredSections, StringComparer.OrdinalIgnoreCase);
    }

    // Checks if a section is registered
    // Returns false if section name is null, empty, or whitespace
    // Thread-safe: Safe to call concurrently with RegisterSection
    public bool IsRegistered(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            return false;

        return _registeredSections.ContainsKey(sectionName);
    }

    // Clears all registered sections
    // WARNING: This method is intended for testing scenarios only
    // In production, registries should not be cleared as it may cause configuration inconsistencies
    // Thread-safe: Safe to call, but may cause enumeration issues if called during GetRegisteredSections
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void Clear()
    {
        // Clear all registrations
        // Note: If another thread is enumerating via GetRegisteredSections(), it will work with a snapshot
        // so the Clear operation won't affect it (defensive copy protects against this)
        _registeredSections.Clear();
    }
}
