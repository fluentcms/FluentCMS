using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FluentCMS.Repositories.EntityFramework;

/// <summary>
/// Intercepts Entity Framework save operations to automatically populate audit fields
/// for entities implementing IAuditableEntity.
/// </summary>
public class AuditableEntitySaveChangesInterceptor : ISaveChangesInterceptor
{
    private readonly IApplicationExecutionContext _executionContext;

    public AuditableEntitySaveChangesInterceptor(IApplicationExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        _executionContext = executionContext;
    }

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
        var username = _executionContext.Username ?? string.Empty;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditableEntity auditableEntity)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        // Set creation audit fields for new entities
                        auditableEntity.CreatedAt = now;
                        auditableEntity.CreatedBy = username;
                        auditableEntity.Version = 1;
                        break;

                    case EntityState.Modified:
                        auditableEntity.UpdatedAt = now;
                        auditableEntity.UpdatedBy = username;
                        auditableEntity.Version += 1;
                        break;
                }
            }
        }
    }
}
