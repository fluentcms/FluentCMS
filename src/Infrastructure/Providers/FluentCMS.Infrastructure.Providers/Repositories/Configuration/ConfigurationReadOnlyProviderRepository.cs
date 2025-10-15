namespace FluentCMS.Infrastructure.Providers.Repositories.Configuration;

public sealed class ConfigurationReadOnlyProviderRepository(IConfiguration configuration, IProviderManager providerManager) : IProviderRepository
{
    private async Task<IEnumerable<Provider>> GetAll(CancellationToken cancellationToken = default)
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

    public IQuerySpecification<Provider> Query()
    {
        var providers = Task.Run(() => GetAll()).GetAwaiter().GetResult();
        return new InMemoryQuerySpecification<Provider>(providers);
    }

    public Task<Provider> Remove(Provider provider, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("In-memory repository does not support removing providers.");
    }

    public Task<Provider> Update(Provider provider, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("In-memory repository does not support updating providers.");
    }

    public Task<Provider> Add(Provider entity, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("In-memory repository does not support adding providers.");
    }

    public Task<IEnumerable<Provider>> AddRange(IEnumerable<Provider> entities, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("In-memory repository does not support adding providers.");
    }

    public Task<Provider> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("In-memory repository does not support removing providers.");
    }

    public Task<IEnumerable<Provider>> RemoveRange(IEnumerable<Provider> entities, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("In-memory repository does not support removing providers.");
    }
}

internal class ProviderAreaConfiguration
{
    public string Name { get; set; } = default!;
    public bool Active { get; set; }
    public string Module { get; set; } = default!;
}


