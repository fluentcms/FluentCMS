namespace FluentCMS.Repositories.MongoDB;

public class LayoutRepository : SiteAssociatedRepository<Layout>, ILayoutRepository
{
    public LayoutRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}
