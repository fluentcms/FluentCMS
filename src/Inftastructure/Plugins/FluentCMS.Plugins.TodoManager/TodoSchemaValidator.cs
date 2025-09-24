using FluentCMS.Database.Abstractions;
using FluentCMS.DataSeeding.Abstractions;
using FluentCMS.Plugins.TodoManager.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.TodoManager;

public class TodoSchemaValidator(TodoDbContext dbContext, IDatabaseManager<ITodoDatabaseMarker> databaseManager) : ISchemaValidator
{
    public int Priority => 10000;

    public async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        await databaseManager.CreateDatabase(cancellationToken);
        var sql = dbContext.Database.GenerateCreateScript();
        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    public async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        if (!await databaseManager.DatabaseExists(cancellationToken))
            return true;
        return !await databaseManager.TablesExist(["Todos"], cancellationToken);
    }
}
