namespace FluentCMS.Infrastructure.Repositories.EntityFramework;

public abstract class BaseDataSeeder<TDbContext>(TDbContext dbContext, ILogger<BaseDataSeeder<TDbContext>> logger) : IDataSeeder
    where TDbContext : DbContext
{
    protected readonly TDbContext DbContext = dbContext;

    public abstract int Priority { get; }

    public virtual async Task<bool> ShouldSeed(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Checking for existing data in {TDbContext}...", typeof(TDbContext).Name);
        var hasData = await DbContext.AnyTablesHaveData(cancellationToken);
        logger.LogInformation("{TDbContext} existing data check result: {Result}", typeof(TDbContext).Name, hasData);
        return !hasData;
    }

    public abstract Task SeedData(CancellationToken cancellationToken = default);
}
