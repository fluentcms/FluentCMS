using FluentCMS.Providers.Abstractions;
using System.Collections.Concurrent;

namespace FluentCMS.Providers;

internal sealed class ProviderModuleCatalogCache
{
    // Area -> (TypeName -> IProviderModule)
    private readonly ConcurrentDictionary<string, Dictionary<string, IProviderModule>> registeredModules = new();

    // Area -> InterfaceType
    private readonly ConcurrentDictionary<string, Type> registeredInterfaces = new();

    // Cache for faster module lookups
    private readonly ConcurrentDictionary<string, IProviderModule> modulesByFullKey = new();

    public ProviderModuleCatalogCache(IEnumerable<IProviderModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var moduleList = modules.ToList();
        ValidateModules(moduleList);

        foreach (var module in moduleList)
        {
            RegisterModule(module);
        }
    }

    private void ValidateModules(IEnumerable<IProviderModule> modules)
    {
        var duplicateModules = modules
            .GroupBy(m => new { m.Area, ModuleType = m.GetType().FullName })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateModules.Any())
        {
            var duplicateList = string.Join(", ", duplicateModules.Select(d => $"{d.Area}:{d.ModuleType}"));
            throw new InvalidOperationException(
                $"Duplicate provider modules found: {duplicateList}");
        }

        // Validate that all modules have valid properties
        foreach (var module in modules)
        {
            ValidateModule(module);
        }
    }

    private static void ValidateModule(IProviderModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (string.IsNullOrWhiteSpace(module.Area))
        {
            throw new InvalidOperationException(
                $"Provider module '{module.GetType().FullName}' has invalid area. Area cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(module.DisplayName))
        {
            throw new InvalidOperationException(
                $"Provider module '{module.GetType().FullName}' has invalid display name. Display name cannot be null or empty.");
        }

        var moduleTypeName = module.GetType().FullName;
        if (string.IsNullOrEmpty(moduleTypeName))
        {
            throw new InvalidOperationException(
                $"Provider module type must have a full name. Module: {module.GetType()}");
        }

        // Validate that the provider type actually implements IProvider
        if (!typeof(IProvider).IsAssignableFrom(module.ProviderType))
        {
            throw new InvalidOperationException(
                $"Provider module '{moduleTypeName}' specifies provider type '{module.ProviderType.FullName}' " +
                $"which does not implement IProvider.");
        }

        // Validate that the interface type is compatible with the provider type
        if (!module.InterfaceType.IsAssignableFrom(module.ProviderType))
        {
            throw new InvalidOperationException(
                $"Provider module '{moduleTypeName}' specifies interface type '{module.InterfaceType.FullName}' " +
                $"which is not implemented by provider type '{module.ProviderType.FullName}'.");
        }

        // Validate options type if specified
        if (module.OptionsType != null)
        {
            try
            {
                // Try to create an instance to validate it has a parameterless constructor
                Activator.CreateInstance(module.OptionsType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Provider module '{moduleTypeName}' specifies options type '{module.OptionsType.FullName}' " +
                    $"which cannot be instantiated. Ensure it has a public parameterless constructor.", ex);
            }
        }
    }

    private void RegisterModule(IProviderModule module)
    {
        var moduleTypeName = module.GetType().FullName!;

        // Register interface type for the area
        registeredInterfaces.AddOrUpdate(module.Area,
            module.InterfaceType,
            (area, existingType) =>
            {
                if (existingType != module.InterfaceType)
                {
                    throw new InvalidOperationException(
                        $"Multiple interface types registered for area '{area}'. " +
                        $"Existing: '{existingType.FullName}', New: '{module.InterfaceType.FullName}'");
                }
                return existingType;
            });

        // Register module by area and type name
        registeredModules.AddOrUpdate(module.Area,
            new Dictionary<string, IProviderModule>(StringComparer.OrdinalIgnoreCase) { [moduleTypeName] = module },
            (area, existingModules) =>
            {
                if (existingModules.ContainsKey(moduleTypeName))
                {
                    throw new InvalidOperationException(
                        $"Provider module '{moduleTypeName}' is already registered for area '{area}'.");
                }
                existingModules[moduleTypeName] = module;
                return existingModules;
            });

        // Add to cache for faster lookups
        var fullKey = $"{module.Area}:{moduleTypeName}";
        modulesByFullKey[fullKey] = module;
    }

    public IEnumerable<IProviderModule> GetRegisteredModules()
    {
        return modulesByFullKey.Values;
    }

    public IReadOnlyDictionary<string, Type> GetRegisteredInterfaceTypes()
    {
        return registeredInterfaces.AsReadOnly();
    }

    public IProviderModule? GetRegisteredModule(string area, string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        var fullKey = $"{area}:{typeName}";
        return modulesByFullKey.TryGetValue(fullKey, out var module) ? module : null;
    }

    public IEnumerable<IProviderModule> GetModulesByArea(string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);

        return registeredModules.TryGetValue(area, out var areaModules)
            ? areaModules.Values
            : [];
    }

    public bool HasModulesInArea(string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        return registeredModules.ContainsKey(area);
    }

    public IEnumerable<string> GetRegisteredAreas()
    {
        return registeredModules.Keys;
    }
}
