namespace FluentCMS.Infrastructure.Providers.Repositories.EntityFramework;

public class ProviderDataSeeder(IProviderManager providerManager, ConfigurationReadOnlyProviderRepository readOnlyProviderRepository, ProviderDbContext dbContext, ILogger<ProviderDataSeeder> logger) : BaseDataSeeder<ProviderDbContext>(dbContext, logger)
{
    public override int Priority => 1;

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providers = await readOnlyProviderRepository.Query().ToList(cancellationToken);
        var providerCatalogs = new List<ProviderCatalog>();
        foreach (var provider in providers)
        {
            var providerModule = await providerManager.GetProviderModule(provider.Area, provider.ModuleType, cancellationToken) ??
                throw new InvalidOperationException($"Provider module '{provider.ModuleType}' for area '{provider.Area}' not found.");

            var providerCatalog = new ProviderCatalog(providerModule, provider.Name, provider.IsActive, provider.Options);
            providerCatalogs.Add(providerCatalog);
        }

        await DbContext.Providers.AddRangeAsync(providers, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
    }
}
