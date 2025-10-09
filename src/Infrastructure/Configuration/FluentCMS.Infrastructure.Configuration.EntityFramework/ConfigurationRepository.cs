namespace FluentCMS.Infrastructure.Configuration.EntityFramework;

internal class ConfigurationRepository(ConfigurationDbContext dataContext) : Repository<ConfigurationEntity, ConfigurationDbContext>(dataContext), IConfigurationRepository
{
}
