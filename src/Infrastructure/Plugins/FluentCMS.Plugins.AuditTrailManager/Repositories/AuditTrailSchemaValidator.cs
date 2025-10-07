namespace FluentCMS.Plugins.AuditTrailManager.Repositories;

public class AuditTrailSchemaValidator(AuditTrailDbContext dbContext, ILogger<AuditTrailSchemaValidator> logger) : BaseSchemaValidator<AuditTrailDbContext>(dbContext, logger)
{
    public override int Priority => 10;
}
