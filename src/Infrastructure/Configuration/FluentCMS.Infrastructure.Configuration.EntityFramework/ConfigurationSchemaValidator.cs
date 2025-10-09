namespace FluentCMS.Infrastructure.Configuration.EntityFramework;

internal class ConfigurationSchemaValidator(ConfigurationDbContext dbContext, ILogger<ConfigurationSchemaValidator> logger) : BaseSchemaValidator<ConfigurationDbContext>(dbContext, logger)
{
    // The configuration schema validator has the lowest priority to ensure it runs last
    public override int Priority => 0;
}
