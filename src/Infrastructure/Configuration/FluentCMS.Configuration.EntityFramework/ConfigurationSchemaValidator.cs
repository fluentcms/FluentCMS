using FluentCMS.Repositories.DataInitialization.EntityFramework;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Configuration.EntityFramework;

internal class ConfigurationSchemaValidator(ConfigurationDbContext dbContext, ILogger<ConfigurationSchemaValidator> logger) : BaseSchemaValidator<ConfigurationDbContext>(dbContext, logger)
{
    // The configuration schema validator has the lowest priority to ensure it runs last
    public override int Priority => 0;
}
