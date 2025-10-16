namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

internal class IdentitySchemaValidator(AppIdentityDbContext dbContext, ILogger<IdentitySchemaValidator> logger) : BaseSchemaValidator<AppIdentityDbContext>(dbContext, logger)
{
    public override int Priority => 0; // Set priority to 0 to run before other validators
}
