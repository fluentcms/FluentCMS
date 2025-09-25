using FluentCMS.Providers.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Providers.Repositories.EntityFramework;

internal class ProviderRepository(ProviderDbContext dbContext) : IProviderRepository
{
    public async Task AddMany(IEnumerable<Provider> providers, CancellationToken cancellationToken = default)
    {
        await dbContext.Providers.AddRangeAsync(providers, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<Provider>> GetAll(CancellationToken cancellationToken = default)
    {
        return await dbContext.Providers.ToListAsync(cancellationToken);
    }

    public async Task Remove(Provider provider, CancellationToken cancellationToken = default)
    {
        dbContext.Providers.Remove(provider);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task Update(Provider provider, CancellationToken cancellationToken = default)
    {
        dbContext.Providers.Update(provider);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
