namespace FluentCMS.Repositories.Postgres.Repositories;

public class ApiTokenRepository(PostgresDbContext context) : AuditableEntityRepository<ApiToken>(context), IApiTokenRepository, IService
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await GetByExpression(x => x.Key == apiKey, cancellationToken);
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Table.FirstOrDefaultAsync(x => x.Name == name,cancellationToken);
    }
}
