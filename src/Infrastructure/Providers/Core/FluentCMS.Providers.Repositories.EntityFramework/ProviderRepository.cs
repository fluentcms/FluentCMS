using FluentCMS.Providers.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Providers.Repositories.EntityFramework;

internal class ProviderRepository(ProviderDbContext dbContext, ILogger<ProviderRepository> logger) : Repository<Provider, ProviderDbContext>(dbContext), IProviderRepository
{
}
