using System.Collections.Concurrent;

namespace FluentCMS.Configuration.Abstractions;

/// <summary>
/// Registry to track configuration sections that should be stored in database
/// </summary>
public class DatabaseConfigurationRegistry
{
    private readonly ConcurrentDictionary<string, Type> _registeredSections = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterSection(string sectionName, Type optionsType)
    {
        _registeredSections.TryAdd(sectionName, optionsType);
    }

    public IReadOnlyDictionary<string, Type> GetRegisteredSections()
    {
        return _registeredSections;
    }

    public bool IsRegistered(string sectionName)
    {
        return _registeredSections.ContainsKey(sectionName);
    }

    public void Clear()
    {
        _registeredSections.Clear();
    }
}
