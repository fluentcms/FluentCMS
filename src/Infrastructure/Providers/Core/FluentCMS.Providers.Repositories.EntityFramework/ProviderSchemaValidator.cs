using FluentCMS.Database.Abstractions;
using FluentCMS.DataSeeding.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Providers.Repositories.EntityFramework;

public class ProviderSchemaValidator(IServiceProvider sp) : ISchemaValidator
{
    private readonly ProviderDbContext? _dbContext = sp.GetService<ProviderDbContext>();
    private readonly IDatabaseManager? _databaseManager = sp.GetService<IDatabaseManager<IProviderDatabaseMarker>>();
    private readonly bool _isActive = sp.GetService<ProviderDbContext>() is not null;

    public int Priority => 1;

    public async Task CreateSchema(CancellationToken cancellationToken = default)
    {
        await _databaseManager!.CreateDatabase(cancellationToken);
        var sql = _dbContext!.Database.GenerateCreateScript();
        await _dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    public async Task<bool> ValidateSchema(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            return true;

        if (!await _databaseManager!.DatabaseExists(cancellationToken))
            return false;
        return await _databaseManager.TablesExist(["Providers"], cancellationToken);
    }
}