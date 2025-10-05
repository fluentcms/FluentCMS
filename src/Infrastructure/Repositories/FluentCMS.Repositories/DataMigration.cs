using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories;

public abstract class DataMigration<TDbContext>(TDbContext dbContext, ILogger<DataMigration<TDbContext>> logger) : IDataMigration
    where TDbContext : DbContext
{
    public abstract int Priority { get; }

    public virtual async Task Migrate(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation("Applying pending migrations for database context {DbContextType}", typeof(TDbContext).Name);
        await dbContext.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied successfully for database context {DbContextType}", typeof(TDbContext).Name);
    }
}
