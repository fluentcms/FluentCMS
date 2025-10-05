using FluentCMS.Repositories.EntityFramework.DataInitialization;

namespace FluentCMS.Plugins.IdentityManager.Repositories;

internal class IdentitySchemaValidator(ApplicationDbContext dbContext, ILogger<IdentitySchemaValidator> logger) : BaseSchemaValidator<ApplicationDbContext>(dbContext, logger)
{
    public override int Priority => 1000;
}

