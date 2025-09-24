namespace FluentCMS.Plugins.AuditTrailManager.Repositories;

public interface IAuditTrailRepository
{
    Task Add(AuditTrail entity, CancellationToken cancellationToken = default);
}

public class AuditTrailRepository(AuditTrailDbContext auditTrailDbContext, IMapper mapper, ILogger<AuditTrailRepository> logger) : IAuditTrailRepository
{
    public async Task Add(AuditTrail entity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation($"Adding audit trail entry. EventType: {entity.EventType}, Timestamp: {entity.Timestamp}, UserId: {entity.Context?.UserId}, TraceId: {entity.Context?.TraceId}");

        try
        {
            var internalEntity = mapper.Map<AuditTrailInternal>(entity);
            await auditTrailDbContext.AuditTrails.AddAsync(internalEntity, cancellationToken);
            await auditTrailDbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation($"Successfully added audit trail entry. Id: {internalEntity.Id}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to add audit trail entry. EventType: {entity.EventType}, UserId: {entity.Context?.UserId}, TraceId: {entity.Context?.TraceId}");
            throw;
        }
    }
}