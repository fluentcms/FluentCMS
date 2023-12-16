using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing PluginDefinition entities in MongoDB.
/// Extends AuditEntityRepository to include audit functionality, suitable for entities with audit requirements.
/// </summary>
public class PluginDefinitionRepository : AuditEntityRepository<PluginDefinition>, IPluginDefinitionRepository
{
    /// <summary>
    /// Initializes a new instance of the PluginDefinitionRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public PluginDefinitionRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

}
