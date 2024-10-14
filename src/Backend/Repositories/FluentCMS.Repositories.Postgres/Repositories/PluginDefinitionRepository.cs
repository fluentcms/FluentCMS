namespace FluentCMS.Repositories.Postgres.Repositories;

public class PluginDefinitionRepository(PostgresDbContext context) : AuditableEntityRepository<PluginDefinition>(context), IPluginDefinitionRepository, IService;

