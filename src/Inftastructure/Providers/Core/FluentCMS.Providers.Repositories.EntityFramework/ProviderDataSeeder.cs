using FluentCMS.Database.Abstractions;
using FluentCMS.DataSeeding.Abstractions;
using FluentCMS.Providers.Repositories.Abstractions;
using FluentCMS.Providers.Repositories.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Providers.Repositories.EntityFramework;

public class ProviderDataSeeder(IServiceProvider sp) : IDataSeeder
{
    private readonly IDatabaseManager? _databaseManager = sp.GetService<IDatabaseManager<IProviderDatabaseMarker>>();
    private readonly IProviderManager? _providerManager = sp.GetService<IProviderManager>();
    private readonly IProviderRepository? _providerRepository = sp.GetService<IProviderRepository>();
    private readonly ConfigurationReadOnlyProviderRepository? _readOnlyProviderRepository = sp.GetService<ConfigurationReadOnlyProviderRepository>();
    private readonly bool _isActive = sp.GetService<ProviderDbContext>() is not null;

    public int Priority => 1;

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        var providers = await _readOnlyProviderRepository!.GetAll(cancellationToken);
        var providerCatalogs = new List<ProviderCatalog>();
        foreach (var provider in providers)
        {
            var providerModule = await _providerManager!.GetProviderModule(provider.Area, provider.ModuleType, cancellationToken) ??
                throw new InvalidOperationException($"Provider module '{provider.ModuleType}' for area '{provider.Area}' not found.");

            var providerCatalog = new ProviderCatalog(providerModule, provider.Name, provider.IsActive, provider.Options);
            providerCatalogs.Add(providerCatalog);
        }

        await _providerRepository!.AddMany(providers, cancellationToken);
    }

    public async Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            return false;

        return !await _databaseManager!.TablesEmpty(["Providers"], cancellationToken);
    }
}
