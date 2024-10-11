namespace FluentCMS.Repositories.RavenDB;

public class ApiTokenRepository(IRavenDBContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ApiToken>(dbContext, apiExecutionContext), IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = dbContext.Store.OpenAsyncSession())
        {
            return await session.Query<ApiToken>().SingleOrDefaultAsync(x => x.Key == apiKey, cancellationToken);
        }
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = dbContext.Store.OpenAsyncSession())
        {
            return await session.Query<ApiToken>().SingleOrDefaultAsync(x => x.Name == name, cancellationToken);
        }
    }
}
