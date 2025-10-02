using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.EntityFramework;

public abstract class EfSchemaValidator<TDbContext>(TDbContext dbContext, ILogger<EfSchemaValidator<TDbContext>> logger) : ISchemaValidator
    where TDbContext : DbContext
{
    protected readonly TDbContext DbContext = dbContext;

    public abstract int Priority { get; }

    public virtual async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating {TDbContext} database schema...", typeof(TDbContext).Name);
        var sql = DbContext.Database.GenerateCreateScript();
        await DbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        logger.LogInformation("{TDbContext} database schema created.", typeof(TDbContext).Name);
    }

    public virtual async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Validating {TDbContext} database schema...", typeof(TDbContext).Name);
        var tableChecker = new EfTableChecker(DbContext);
        var result = await tableChecker.AnyTablesExist(cancellationToken);
        logger.LogInformation("{TDbContext} database schema validation result: {Result}", typeof(TDbContext).Name, result);
        return result;
    }
}
