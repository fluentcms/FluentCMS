namespace FluentCMS.Repositories.EntityFramework.Interceptors;

public abstract class BaseSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        var context = eventData.Context;

        // Check if dbContext implements IEventPublisherDbContext
        if (context != null)
        {
            BeforeSaveChanges(eventData).Wait();
            var saveChangeResult = base.SavingChanges(eventData, result);
            AfterSaveChanges(eventData).Wait();
            return saveChangeResult;
        }
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        // Check if dbContext implements IEventPublisherDbContext
        if (context != null)
        {
            await BeforeSaveChanges(eventData, cancellationToken);
            var saveChangeResult = await base.SavingChangesAsync(eventData, result, cancellationToken);
            await AfterSaveChanges(eventData, cancellationToken);
            return saveChangeResult;
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public virtual Task BeforeSaveChanges(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        // Default implementation does nothing
        return Task.CompletedTask;
    }

    public virtual Task AfterSaveChanges(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        // Default implementation does nothing
        return Task.CompletedTask;
    }
}
