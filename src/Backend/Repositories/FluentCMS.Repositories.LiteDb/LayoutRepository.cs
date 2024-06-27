namespace FluentCMS.Repositories.LiteDb;

public class LayoutRepository : AuditableEntityRepository<Layout>, ILayoutRepository
{
    public LayoutRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
}
