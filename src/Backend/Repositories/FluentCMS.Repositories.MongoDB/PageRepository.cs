namespace FluentCMS.Repositories.MongoDB;

public class PageRepository : SiteAssociatedRepository<Page>, IPageRepository
{
    public PageRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}
