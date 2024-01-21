namespace FluentCMS.Repositories.MongoDB;

public class AppTemplateRepository : AuditableEntityRepository<AppTemplate>, IAppTemplateRepository
{
    public AppTemplateRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}
