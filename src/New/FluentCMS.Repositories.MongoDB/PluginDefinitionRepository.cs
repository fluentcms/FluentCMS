namespace FluentCMS.Repositories.MongoDB;

public class PluginDefinitionRepository : AuditableEntityRepository<PluginDefinition>, IPluginDefinitionRepository
{
    public PluginDefinitionRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

}
