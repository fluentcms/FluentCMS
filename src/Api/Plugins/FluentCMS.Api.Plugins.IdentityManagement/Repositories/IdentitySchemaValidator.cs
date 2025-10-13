namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

internal class IdentitySchemaValidator(ApplicationDbContext dbContext, ILogger<IdentitySchemaValidator> logger) : BaseSchemaValidator<ApplicationDbContext>(dbContext, logger)
{
    public override int Priority => 1000;
}

