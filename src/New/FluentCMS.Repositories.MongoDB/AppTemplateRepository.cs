namespace FluentCMS.Repositories.MongoDB;

public class AppTemplateRepository : EntityRepository<AppTemplate>, IAppTemplateRepository
{
    public AppTemplateRepository(IMongoDBContext mongoDbContext) : base(mongoDbContext)
    {
    }
}
