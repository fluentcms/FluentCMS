using System.Collections.Concurrent;

// Namespace for configuration abstractions
namespace FluentCMS.Configuration.Abstractions;

// Registry class to track configuration sections that should be stored in database (static instance used in extensions)
public class DatabaseConfigurationRegistry
{
    // Thread-safe dictionary to store registered sections with their types
    private readonly ConcurrentDictionary<string, Type> _registeredSections = new(StringComparer.OrdinalIgnoreCase);

    // Registers a configuration section with its options type
    public void RegisterSection(string sectionName, Type optionsType)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            throw new ArgumentException("Section name cannot be null or empty", nameof(sectionName));

        ArgumentNullException.ThrowIfNull(optionsType);

        _registeredSections.TryAdd(sectionName, optionsType);
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
