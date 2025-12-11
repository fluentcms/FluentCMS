using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

public interface ISiteRepository : IRepository<Site>
{
    Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default);
}

internal class SiteRepository(CmsCoreDbContext dbContext) : Repository<Site, CmsCoreDbContext>(dbContext), ISiteRepository
{
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return (await Query().ToList(cancellationToken))
            .FirstOrDefault(s => s.Urls.Any(u => u.Contains(url, StringComparison.OrdinalIgnoreCase)));

    }
}
