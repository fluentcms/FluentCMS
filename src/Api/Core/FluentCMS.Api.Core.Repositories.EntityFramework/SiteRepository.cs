namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class SiteRepository(CmsCoreDbContext dbContext) : RepositoryBase<Site>(dbContext), ISiteRepository
{
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return await Query().Where(x => x.Urls.Contains(url)).SingleOrDefault(cancellationToken);
    }
}
