using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.TodoManager.Repositories;

internal class TodoSchemaValidator(TodoDbContext dbContext, ILogger<TodoSchemaValidator> logger) : ISchemaValidator
{
    public int Priority => 10000;

    public async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating TodoManager database schema...");
        var sql = dbContext.Database.GenerateCreateScript();
        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    public async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Validating TodoManager database schema...");
        return await dbContext.Database.CanConnectAsync(cancellationToken);
    }
}
