namespace FluentCMS.Providers.Repositories.EntityFramework;

internal class ProviderRepository(ProviderDbContext dbContext) : Repository<Provider, ProviderDbContext>(dbContext), IProviderRepository
{
}
