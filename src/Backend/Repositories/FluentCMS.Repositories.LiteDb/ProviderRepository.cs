namespace FluentCMS.Repositories.LiteDb;

public class ProviderRepository : AuditableEntityRepository<Provider>, IProviderRepository
{
    public ProviderRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
}
