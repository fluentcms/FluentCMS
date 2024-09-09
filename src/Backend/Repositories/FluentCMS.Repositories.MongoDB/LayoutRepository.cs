namespace FluentCMS.Repositories.MongoDB;

public class LayoutRepository : SiteAssociatedRepository<Layout>, ILayoutRepository
{
    public LayoutRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }
}
