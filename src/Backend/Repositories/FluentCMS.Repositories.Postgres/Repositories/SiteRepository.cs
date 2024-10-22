namespace FluentCMS.Repositories.Postgres.Repositories;

public class SiteRepository(PostgresDbContext context) : AuditableEntityRepository<Site>(context), ISiteRepository, IService
{
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await GetByExpression(x => x.Urls.Contains(url),cancellationToken);
    }
}
