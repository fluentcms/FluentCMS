namespace FluentCMS.Repositories.RavenDB;

public class PageRepository : SiteAssociatedRepository<Page>, IPageRepository
{
    public PageRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : base(RavenDbContext, apiExecutionContext)
    {
    }
}
