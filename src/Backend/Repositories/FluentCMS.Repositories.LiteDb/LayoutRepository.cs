namespace FluentCMS.Repositories.LiteDb;

public class LayoutRepository : SiteAssociatedRepository<Layout>, ILayoutRepository
{
    public LayoutRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext, apiExecutionContext)
    {
    }
}
