namespace FluentCMS.Plugins.IdentityManager;

public class IdentitySchemaValidator(ApplicationDbContext dbContext, IDatabaseManager<IIdentityDatabaseMarker> databaseManager) : ISchemaValidator
{
    public int Priority => 1000;

    public async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        await databaseManager.CreateDatabase(cancellationToken);
        var sql = dbContext.Database.GenerateCreateScript();
        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    public async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        if (!await databaseManager.DatabaseExists(cancellationToken))
            return false;
        return await databaseManager.TablesExist(["Users", "Roles"], cancellationToken);
    }
}

