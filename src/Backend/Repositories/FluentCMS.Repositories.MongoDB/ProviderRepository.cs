namespace FluentCMS.Repositories.MongoDB;

public class ProviderRepository : AuditableEntityRepository<Provider>, IProviderRepository
{
    public ProviderRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}
