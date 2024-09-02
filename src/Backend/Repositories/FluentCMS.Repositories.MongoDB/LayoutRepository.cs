namespace FluentCMS.Repositories.MongoDB;

public class LayoutRepository : AuditableEntityRepository<Layout>, ILayoutRepository
{
    public LayoutRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }
}
