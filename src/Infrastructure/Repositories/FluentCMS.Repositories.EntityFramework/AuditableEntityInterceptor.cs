//namespace FluentCMS.Repositories.EntityFramework;

///// <summary>
///// An EF Core SaveChangesInterceptor to automatically set auditing properties on entities implementing IAuditableEntity.
///// We are not sure other required services are available in the DbContext, so we use IServiceProvider to resolve them.
///// </summary>
//public class AuditableEntityInterceptor(IServiceProvider serviceProvider) : SaveChangesInterceptor
//{
//    // SavingChanges and SavingChangesAsync are not linked—EF Core calls one or the other depending on which SaveChanges overload the application invokes:
//    // SavingChanges is invoked only when you(or EF itself) call a synchronous method such as context.SaveChanges().
//    // SavingChangesAsync is invoked only when you call an asynchronous method such as await context.SaveChangesAsync().
//    // EF Core does not fall back from one to the other.

//    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
//    {
//        ApplyAuditing(eventData).GetAwaiter().GetResult();
//        return base.SavingChanges(eventData, result);
//    }

//    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
//    {
//        await ApplyAuditing(eventData);
//        return await base.SavingChangesAsync(eventData, result, cancellationToken);
//    }

//    private async Task ApplyAuditing(DbContextEventData eventData)
//    {
//        var executionContext = serviceProvider.GetService<IApplicationExecutionContext>();
//        var logger = serviceProvider.GetService<ILogger<AuditableEntityInterceptor>>();

//        try
//        {
//            if (executionContext == null)
//            {
//                // Log warning that execution context is not available            
//                logger?.LogWarning("IApplicationExecutionContext is not available for auditing");
//            }

//            var now = DateTime.UtcNow;
//            var audits = new List<AuditTrail>();

//            if (eventData.Context == null)
//                return;

//            foreach (var entry in eventData.Context.ChangeTracker.Entries<IAuditableEntity>())
//            {
//                var auditable = entry.Entity;
//                if (auditable == null)
//                    continue;

//                var typeName = auditable.GetType().Name;
//                switch (entry.State)
//                {
//                    case EntityState.Added:
//                        // For new entities, set the CreatedAt and CreatedBy properties
//                        // Also set the Version to 1
//                        auditable.CreatedAt = now;
//                        auditable.CreatedBy = executionContext?.Username;
//                        auditable.UpdatedAt = null;
//                        auditable.UpdatedBy = null;
//                        auditable.Version = 1;
//                        audits.Add(new AuditTrail
//                        {
//                            Id = Guid.NewGuid(),
//                            EventType = $"{typeName}.Added",
//                            Timestamp = DateTime.UtcNow,
//                            Entity = auditable,
//                            Context = executionContext
//                        });
//                        break;

//                    case EntityState.Modified:
//                        // For modified entities, set the UpdatedAt and UpdatedBy properties
//                        // Also increment the Version
//                        auditable.UpdatedBy = executionContext?.Username;
//                        auditable.UpdatedAt = DateTime.UtcNow;
//                        auditable.Version++;

//                        audits.Add(new AuditTrail
//                        {
//                            Id = Guid.NewGuid(),
//                            EventType = $"{typeName}.Modified",
//                            Timestamp = DateTime.UtcNow,
//                            Entity = auditable,
//                            Context = executionContext,
//                        });
//                        break;

//                    case EntityState.Deleted:
//                        audits.Add(new AuditTrail
//                        {
//                            Id = Guid.NewGuid(),
//                            EventType = $"{typeName}.Deleted",
//                            Timestamp = DateTime.UtcNow,
//                            Entity = auditable,
//                            Context = executionContext,
//                        });

//                        break;
//                }
//            }

//            if (audits.Count > 0)
//            {
//                var auditRepository = serviceProvider.GetService<IAuditTrailRepository>();
//                if (auditRepository == null)
//                {
//                    logger?.LogWarning("Audit trail repository is not available for auditing");
//                    return;
//                }
//                await auditRepository.AddRange(audits);
//            }

//        }
//        catch (Exception)
//        {
//            logger?.LogError("Failed to apply auditing in AuditableEntityInterceptor");
//            throw;
//        }
//    }
//}
