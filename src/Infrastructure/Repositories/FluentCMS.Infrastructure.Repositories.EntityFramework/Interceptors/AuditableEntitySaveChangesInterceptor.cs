namespace FluentCMS.Infrastructure.Repositories.EntityFramework.Interceptors;

/// <summary>
/// Intercepts Entity Framework save operations to automatically populate audit fields
/// for entities implementing IAuditableEntity.
/// </summary>
public class AuditableEntitySaveChangesInterceptor(IApplicationExecutionContext executionContext) : ISaveChangesInterceptor
{
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateAuditFields(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateAuditFields(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void UpdateAuditFields(DbContext? context)
    {
        if (context == null)
            return;

        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditableEntity auditableEntity)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        // Set creation audit fields for new entities
                        auditableEntity.CreatedAt = now;
                        auditableEntity.CreatedBy = executionContext.Username;
                        auditableEntity.Version = 1;
                        break;

                    case EntityState.Modified:
                        auditableEntity.UpdatedAt = now;
                        auditableEntity.UpdatedBy = executionContext.Username;
                        auditableEntity.Version += 1;
                        break;
                }
            }
        }
    }
}
