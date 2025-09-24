using FluentCMS.Providers.Repositories.Abstractions;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace FluentCMS.Providers.Repositories.Configuration;

public sealed class ConfigurationReadOnlyProviderRepository(IConfiguration configuration, IProviderManager providerManager) : IProviderRepository
{
    public async Task<IEnumerable<Provider>> GetAll(CancellationToken cancellationToken = default)
    {
        var providers = new List<Provider>();
        var providerAreas = configuration.GetSection("Providers").GetChildren();
        foreach (var areaSection in providerAreas)
        {
            var areaName = areaSection.Key;
            var providersSection = areaSection.GetChildren();
            foreach (var providerSection in providersSection)
            {
                var providerConfig = providerSection.Get<ProviderAreaConfiguration>() ??
                    throw new InvalidOperationException($"Invalid provider configuration for area '{areaName}'.");

                var module = await providerManager.GetProviderModule(areaName, providerConfig.Module, cancellationToken) ??
                    throw new InvalidOperationException($"Provider module '{providerConfig.Module}' for area '{areaName}' not found.");

                var optionsSection = providerSection.GetSection("Options");
                object? options = null;
                if (module.OptionsType != null)
                {
                    options = optionsSection.Get(module.OptionsType) ?? Activator.CreateInstance(module.OptionsType);
                }

                providers.Add(new Provider
                {
                    Area = areaName,
                    Name = providerConfig.Name,
                    DisplayName = module.DisplayName,
                    IsActive = providerConfig.Active,
                    ModuleType = providerConfig.Module,
                    Options = options is null ? null : JsonSerializer.Serialize(options)
                });
            }
        }
        return await Task.FromResult(providers);
    }

    public Task Remove(Provider provider, CancellationToken cancellationToken = default)
    {
        throw new Exception("In-memory repository does not support removing providers.");
    }

    public Task Update(Provider provider, CancellationToken cancellationToken = default)
    {
        throw new Exception("In-memory repository does not support updating providers.");
    }

    public Task AddMany(IEnumerable<Provider> providers, CancellationToken cancellationToken = default)
    {
        throw new Exception("In-memory repository does not support adding providers.");
    }
}

internal class ProviderAreaConfiguration
{
    public string Name { get; set; } = default!;
    public bool Active { get; set; }
    public string Module { get; set; } = default!;
}
