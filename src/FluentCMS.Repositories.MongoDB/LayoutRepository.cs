namespace FluentCMS.Repositories.MongoDB;

public class LayoutRepository : AuditableEntityRepository<Layout>, ILayoutRepository
{
    public LayoutRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}
