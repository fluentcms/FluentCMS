namespace FluentCMS.Repositories.MongoDB;

public class PluginDefinitionRepository : AuditableEntityRepository<PluginDefinition>, IPluginDefinitionRepository
{
    public PluginDefinitionRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : base(mongoDbContext, apiExecutionContext)
    {
    }

}
