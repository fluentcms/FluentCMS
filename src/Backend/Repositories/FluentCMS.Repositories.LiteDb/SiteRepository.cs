namespace FluentCMS.Repositories.LiteDb;

public class SiteRepository : AuditableEntityRepository<Site>, ISiteRepository
{
    public SiteRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext, apiExecutionContext)
    {
    }

    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Collection.Query().Where(x => x.Urls.Contains(url)).SingleOrDefaultAsync();
    }
}
