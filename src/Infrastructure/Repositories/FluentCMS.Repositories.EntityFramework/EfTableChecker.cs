using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.EntityFramework;

internal class EfTableChecker(DbContext dbContext)
{
    public async Task<bool> AnyTablesHaveData(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelTypes = dbContext.Model.GetEntityTypes().Select(x => x.ClrType).Where(t => t != null).Distinct();
        // any table has data
        foreach (var modelType in modelTypes)
        {
            if (await HasAnyData(modelType, cancellationToken))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<bool> AnyTablesExist(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelTypes = dbContext.Model.GetEntityTypes().Select(x => x.ClrType).Where(t => t != null).Distinct();

        // any table has data
        foreach (var modelType in modelTypes)
        {
            try
            {
                await HasAnyData(modelType, cancellationToken);
                return true;
            }
            catch 
            {
                // Intentionally suppressed - checking for table existence
            }
        }
        return false;
    }

    private static bool ShouldSuppressLogging() => true;


    public async Task<bool> HasAnyData(Type modelType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Get the DbSet using context.Set<T>()
        var setMethod = typeof(DbContext)
            .GetMethod(nameof(DbContext.Set), Type.EmptyTypes)
            ?.MakeGenericMethod(modelType) ??
            throw new InvalidOperationException($"DbContext.Set<{modelType.Name}>() method not found");

        var dbSet = setMethod.Invoke(dbContext, null)
            ?? throw new InvalidOperationException($"DbSet for {modelType.Name} could not be created.");

        // Call AnyAsync using reflection
        var anyAsyncMethod = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .Where(m => m.Name == "AnyAsync" && m.GetParameters().Length == 2)
                .FirstOrDefault()
                ?.MakeGenericMethod(modelType) ??
                throw new InvalidOperationException($"AnyAsync<{modelType.Name}> method not found");

        var task = anyAsyncMethod.Invoke(null, [dbSet, cancellationToken])
            ?? throw new InvalidOperationException($"AnyAsync invocation for {modelType.Name} returned null.");

        var hasData = await (Task<bool>)task;
        return hasData;
    }
}
