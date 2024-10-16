namespace FluentCMS.Repositories.RavenDB;

public class PluginDefinitionRepository : AuditableEntityRepository<PluginDefinition>, IPluginDefinitionRepository
{
    public PluginDefinitionRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : base(RavenDbContext, apiExecutionContext)
    {
    }

}
