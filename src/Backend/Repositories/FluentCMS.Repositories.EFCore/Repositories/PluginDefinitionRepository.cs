namespace FluentCMS.Repositories.EFCore;

public class PluginDefinitionRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<PluginDefinition, PluginDefinitionModel>(dbContext, mapper, apiExecutionContext), IPluginDefinitionRepository;
