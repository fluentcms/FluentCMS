namespace FluentCMS.Api.Plugins.AuditTrailManagement.Services;

public interface IAuditTrailService
{
    Task Add(object entity, string eventType, CancellationToken cancellationToken = default);
}

public class AuditTrailService(IAuditTrailRepository repository, ISecurityContext securityContext) : IAuditTrailService
{
    public async Task Add(object entity, string eventType, CancellationToken cancellationToken = default)
    {
        if (entity is IAuditableEntity auditableEntity)
        {
            var auditTrail = new AuditTrail
            {
                EventType = eventType,
                Timestamp = DateTime.UtcNow,
                Entity = auditableEntity,
                Context = securityContext,
            };
            await repository.Add(auditTrail, cancellationToken);
        }
    }
}
