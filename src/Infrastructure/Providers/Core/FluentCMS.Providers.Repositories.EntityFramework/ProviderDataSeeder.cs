using FluentCMS.Providers.Repositories.Configuration;
using FluentCMS.Repositories.EntityFramework;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Providers.Repositories.EntityFramework;

public class ProviderDataSeeder(IProviderManager providerManager, ConfigurationReadOnlyProviderRepository readOnlyProviderRepository, ProviderDbContext dbContext, ILogger<ProviderDataSeeder> logger) : EfDataSeeder<ProviderDbContext>(dbContext, logger)
{
    public override int Priority => 1;

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providers = await readOnlyProviderRepository.Query().ToEnumerable(cancellationToken);
        var providerCatalogs = new List<ProviderCatalog>();
        foreach (var provider in providers)
        {
            var providerModule = await providerManager.GetProviderModule(provider.Area, provider.ModuleType, cancellationToken) ??
                throw new InvalidOperationException($"Provider module '{provider.ModuleType}' for area '{provider.Area}' not found.");

            var providerCatalog = new ProviderCatalog(providerModule, provider.Name, provider.IsActive, provider.Options);
            providerCatalogs.Add(providerCatalog);
        }

        await dbContext.Providers.AddRangeAsync(providers, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
