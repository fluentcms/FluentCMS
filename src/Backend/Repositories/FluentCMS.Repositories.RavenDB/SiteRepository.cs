namespace FluentCMS.Repositories.RavenDB;

public class SiteRepository : AuditableEntityRepository<Site>, ISiteRepository
{
    public SiteRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : base(RavenDbContext, apiExecutionContext)
    {
    }
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            return await session.Query<Site>().SingleOrDefaultAsync(x => x.Urls.Contains(url), cancellationToken);
        }
    }
}
