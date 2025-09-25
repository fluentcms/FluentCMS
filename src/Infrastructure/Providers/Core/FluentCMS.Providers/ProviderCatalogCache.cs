using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace FluentCMS.Providers;

internal sealed class ProviderCatalogCache
{
    private readonly ConcurrentDictionary<string, ProviderCatalog> _catalogsByKey = new();
    private readonly ConcurrentDictionary<string, ImmutableList<ProviderCatalog>> _catalogsByArea = new();
    private readonly ConcurrentDictionary<string, ProviderCatalog> _activeCatalogs = new();
    private volatile bool _isInitialized = false;
    private readonly Lock _lock = new();

    public bool IsInitialized => _isInitialized;

    public void Initialize(IEnumerable<ProviderCatalog> catalogs)
    {
        lock (_lock)
        {
            if (_isInitialized)
                throw new InvalidOperationException("ProviderCatalogCache is already initialized.");

            foreach (var catalog in catalogs)
            {
                AddCatalogInternal(catalog);
            }
            _isInitialized = true;
        }
    }

    public void Reload(IEnumerable<ProviderCatalog> catalogs)
    {
        lock (_lock)
        {
            Clear();
            foreach (var catalog in catalogs)
            {
                AddCatalogInternal(catalog);
            }
            _isInitialized = true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _catalogsByKey.Clear();
            _catalogsByArea.Clear();
            _activeCatalogs.Clear();
            _isInitialized = false;
        }
    }

    public void AddCatalog(ProviderCatalog providerCatalog)
    {
        lock (_lock)
        {
            if (_isInitialized)
                throw new InvalidOperationException("Cannot add catalogs after initialization. Use Reload() instead.");

            AddCatalogInternal(providerCatalog);
        }
    }

    private void AddCatalogInternal(ProviderCatalog providerCatalog)
    {
        var area = providerCatalog.Module.Area;
        var key = $"{area}:{providerCatalog.Name}";

        // Add to key-based lookup
        _catalogsByKey[key] = providerCatalog;

        // Update active catalog if this provider is active
        if (providerCatalog.Active)
        {
            if (_activeCatalogs.TryGetValue(area, out ProviderCatalog? value))
            {
                throw new InvalidOperationException(
                    $"Multiple active providers found for area '{area}'. " +
                    $"Existing: '{value.Name}', New: '{providerCatalog.Name}'");
            }
            _activeCatalogs[area] = providerCatalog;
        }

        // Update area-based collection using thread-safe immutable operations
        _catalogsByArea.AddOrUpdate(area,
            [providerCatalog],
            (_, existing) => existing.Add(providerCatalog));
    }

    public ProviderCatalog? GetActiveCatalog(string area)
    {
        _activeCatalogs.TryGetValue(area, out var catalog);
        return catalog;
    }

    public ProviderCatalog? GetCatalog(string area, string providerName)
    {
        var key = $"{area}:{providerName}";
        _catalogsByKey.TryGetValue(key, out var catalog);
        return catalog;
    }

    public IReadOnlyList<ProviderCatalog> GetCatalogsByArea(string area)
    {
        return _catalogsByArea.TryGetValue(area, out var catalogs)
            ? catalogs
            : [];
    }
}
