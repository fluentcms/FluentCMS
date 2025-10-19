namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

internal class CmsCoreSchemaValidator(CmsCoreDbContext dbContext, ILogger<CmsCoreSchemaValidator> logger) : BaseSchemaValidator<CmsCoreDbContext>(dbContext, logger)
{
    public override int Priority => 1;
}
