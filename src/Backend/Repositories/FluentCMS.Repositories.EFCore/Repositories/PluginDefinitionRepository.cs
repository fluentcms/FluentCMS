namespace FluentCMS.Repositories.EFCore;

public class PluginDefinitionRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<PluginDefinition>(dbContext, apiExecutionContext), IPluginDefinitionRepository;
