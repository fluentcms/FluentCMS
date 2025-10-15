namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class PluginDefinitionRepository(CmsCoreDbContext dbContext) : RepositoryBase<PluginDefinition>(dbContext), IPluginDefinitionRepository;
