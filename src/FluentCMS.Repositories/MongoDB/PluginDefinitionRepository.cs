using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

public class PluginDefinitionRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    AuditEntityRepository<PluginDefinition>(mongoDbContext, applicationContext),
    IPluginDefinitionRepository
{

}
