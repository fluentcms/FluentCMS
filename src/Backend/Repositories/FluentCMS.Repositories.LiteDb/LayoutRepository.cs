namespace FluentCMS.Repositories.LiteDb;

public class LayoutRepository : AuditableEntityRepository<Layout>, ILayoutRepository
{
    public LayoutRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : base(liteDbContext, apiExecutionContext)
    {
    }
}
