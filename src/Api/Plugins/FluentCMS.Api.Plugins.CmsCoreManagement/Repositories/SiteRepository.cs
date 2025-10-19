namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

public interface ISiteRepository : IRepository<Site>
{
    Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default);
}

internal class SiteRepository(CmsCoreDbContext dbContext) : Repository<Site, CmsCoreDbContext>(dbContext), ISiteRepository
{
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return await Query().Where(x => x.Urls.Contains(url)).SingleOrDefault(cancellationToken);
    }
}
