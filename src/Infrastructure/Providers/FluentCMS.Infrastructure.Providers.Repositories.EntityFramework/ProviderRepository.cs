namespace FluentCMS.Infrastructure.Providers.Repositories.EntityFramework;

internal class ProviderRepository(ProviderDbContext dbContext) : Repository<Provider, ProviderDbContext>(dbContext), IProviderRepository
{
}
