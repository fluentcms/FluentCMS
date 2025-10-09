using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework;

namespace FluentCMS.Configuration.EntityFramework;

public interface IConfigurationRepository : IRepository<ConfigurationEntity>
{
}

internal class ConfigurationRepository(ConfigurationDbContext dataContext) : Repository<ConfigurationEntity, ConfigurationDbContext>(dataContext), IConfigurationRepository
{
}
