namespace FluentCMS.Infrastructure.Repositories.EntityFramework;

public static class DbContextExtensions
{
    public static async Task<bool> AnyTablesHaveData(this DbContext dbContext, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelTypes = dbContext.Model.GetEntityTypes().Select(x => x.ClrType).Where(t => t != null).Distinct();
        // any table has data
        foreach (var modelType in modelTypes)
        {
            if (await dbContext.HasAnyData(modelType, cancellationToken))
            {
                return true;
            }
        }
        return false;
    }

    public static async Task<bool> AnyTablesExist(this DbContext dbContext, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelTypes = dbContext.Model.GetEntityTypes().Select(x => x.ClrType).Where(t => t != null).Distinct();
        // any table has data
        foreach (var modelType in modelTypes)
        {
            try
            {
                await dbContext.HasAnyData(modelType, cancellationToken);
                return true;
            }
            catch
            {
                // ignore
            }
        }
        return false;
    }

    public static async Task<bool> HasAnyData(this DbContext dbContext, Type modelType, CancellationToken cancellationToken = default)
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
