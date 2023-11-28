using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

public class PluginDefinitionRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    GenericRepository<PluginDefinition>(mongoDbContext, applicationContext),
    IPluginDefinitionRepository
{

}
