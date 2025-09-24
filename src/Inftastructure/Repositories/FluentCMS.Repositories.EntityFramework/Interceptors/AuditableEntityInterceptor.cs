namespace FluentCMS.Repositories.EntityFramework.Interceptors;

public class AuditableEntityInterceptor(IApplicationExecutionContext executionContext) : BaseSaveChangesInterceptor
{
    public override Task BeforeSaveChanges(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        foreach (var entry in eventData.Context!.ChangeTracker.Entries<IAuditableEntity>())
        {
            var entity = entry.Entity;
            switch (entry.State)
            {
                case EntityState.Added:
                    // For new entities, set the CreatedAt and CreatedBy properties
                    // Also set the Version to 1
                    entity.CreatedBy = executionContext.Username;
                    entity.CreatedAt = DateTime.UtcNow;
                    entity.Version = 1;
                    break;

                case EntityState.Modified:
                    // For modified entities, set the UpdatedAt and UpdatedBy properties
                    // Also increment the Version
                    entity.UpdatedBy = executionContext.Username;
                    entity.UpdatedAt = DateTime.UtcNow;
                    entity.Version++;

                    break;

                case EntityState.Deleted:
                    // Do nothing                    
                    break;
            }
        }
        return Task.CompletedTask;
    }
}
