namespace FluentCMS.Repositories.EFCore;

public class SiteRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<Site>(dbContext, apiExecutionContext), ISiteRepository
{
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return await DbContext.Sites.Where(x => x.Urls.Contains(url)).SingleOrDefaultAsync(cancellationToken);
    }
}
