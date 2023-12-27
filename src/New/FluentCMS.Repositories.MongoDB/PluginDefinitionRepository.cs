namespace FluentCMS.Repositories.MongoDB;

public class PluginDefinitionRepository : EntityRepository<PluginDefinition>, IPluginDefinitionRepository
{
    public PluginDefinitionRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

}
