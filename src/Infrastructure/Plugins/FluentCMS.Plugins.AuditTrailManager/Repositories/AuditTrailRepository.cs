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

        logger.LogInformation(
            "Adding audit trail entry. EventType: {EventType}, Timestamp: {Timestamp}, UserId: {UserId}, TraceId: {TraceId}",
            entity.EventType,
            entity.Timestamp,
            entity.Context?.UserId,
            entity.Context?.TraceId);

        try
        {
            var internalEntity = mapper.Map<AuditTrailInternal>(entity);
            await auditTrailDbContext.AuditTrails.AddAsync(internalEntity, cancellationToken);
            await auditTrailDbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Successfully added audit trail entry. Id: {Id}",
                internalEntity.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to add audit trail entry. EventType: {EventType}, UserId: {UserId}, TraceId: {TraceId}",
                entity.EventType,
                entity.Context?.UserId,
                entity.Context?.TraceId);
            throw;
        }
    }
}
