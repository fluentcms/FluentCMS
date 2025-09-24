using FluentCMS.Providers.Abstractions;
using FluentCMS.Providers.Repositories.Abstractions;
using System.Text.Json;

namespace FluentCMS.Providers;

public interface IProviderManager
{
    Task<IProviderModule?> GetProviderModule(string area, string typeName, CancellationToken cancellationToken = default!);
    Task<ProviderCatalog?> GetActiveByArea(string area, CancellationToken cancellationToken = default);
}

internal sealed class ProviderManager(ProviderCatalogCache providerCatalogCache, ProviderModuleCatalogCache providerModuleCatalogCache, IProviderRepository repository) : IProviderManager
{
    public async Task<ProviderCatalog?> GetActiveByArea(string area, CancellationToken cancellationToken = default)
    {
        await Initialize(cancellationToken);
        return providerCatalogCache.GetActiveCatalog(area);
    }

    public Task<IProviderModule?> GetProviderModule(string area, string typeName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(providerModuleCatalogCache.GetRegisteredModule(area, typeName));
    }

    private async Task Initialize(CancellationToken cancellationToken = default)
    {
        if (providerCatalogCache.IsInitialized)
            return;

        var providers = await repository.GetAll(cancellationToken);
        IEnumerable<ProviderCatalog> providerCatalogs = [];

        foreach (var provider in providers)
        {
            var module = providerModuleCatalogCache.GetRegisteredModule(provider.Area, provider.ModuleType) ??
                throw new InvalidOperationException($"Provider module '{provider.ModuleType}' for area '{provider.Area}' not found.");

            object? options = null;
            if (module.OptionsType != null)
            {
                if (string.IsNullOrEmpty(provider.Options))
                    options = Activator.CreateInstance(module.OptionsType);
                else
                    options = JsonSerializer.Deserialize(provider.Options, module.OptionsType);
            }
            var catalog = new ProviderCatalog(module, provider.Name, provider.IsActive, options);
            providerCatalogs = providerCatalogs.Append(catalog);
        }

        // Check if there are multiple active providers in the same area, if exists throw exception
        var activeGroups = providerCatalogs
            .Where(pc => pc.Active)
            .GroupBy(pc => pc.Module.Area)
            .Where(g => g.Count() > 1)
            .ToList();

        if (activeGroups.Count != 0)
        {
            var errorMessage = string.Join(", ", activeGroups.Select(g => $"Area '{g.Key}' has multiple active providers: {string.Join(", ", g.Select(pc => pc.Name))}"));
            throw new InvalidOperationException($"Provider error: {errorMessage}");
        }

        providerCatalogCache.Initialize(providerCatalogs);
    }
}

