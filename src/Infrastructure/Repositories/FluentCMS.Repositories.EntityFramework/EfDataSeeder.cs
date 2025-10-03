using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.EntityFramework;

public abstract class EfDataSeeder<TDbContext>(TDbContext dbContext, ILogger<EfDataSeeder<TDbContext>> logger) : IDataSeeder
    where TDbContext : DbContext
{
    protected readonly TDbContext DbContext = dbContext;

    public abstract int Priority { get; }

    public virtual async Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Checking for existing data in {TDbContext}...", typeof(TDbContext).Name);
        var tableChecker = new EfTableChecker(DbContext);
        var result = await tableChecker.AnyTablesHaveData(cancellationToken);
        logger.LogInformation("{TDbContext} existing data check result: {Result}", typeof(TDbContext).Name, result);
        return result;
    }

    public abstract Task SeedData(CancellationToken cancellationToken = default);
}

