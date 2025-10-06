using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.EntityFramework.Interceptors;

/// <summary>
/// Intercepts Entity Framework save operations to automatically populate audit fields
/// for entities implementing IAuditableEntity.
/// </summary>
public class AuditableEntitySaveChangesInterceptor(IServiceProvider serviceProvider, ILogger<AuditableEntitySaveChangesInterceptor> logger) : ISaveChangesInterceptor
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

        var executionContext = serviceProvider.GetService<IApplicationExecutionContext>();
        if (executionContext == null)
            logger.LogWarning("IApplicationExecutionContext service is not registered. Audit fields will use empty username.");

        var now = DateTime.UtcNow;
        var username = executionContext?.Username ?? string.Empty;

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
