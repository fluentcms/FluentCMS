namespace FluentCMS.Repositories.RavenDB;

public class ApiTokenRepository(IRavenDBContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ApiToken>(dbContext, apiExecutionContext), IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entity = await session.Query<RavenEntity<ApiToken>>().SingleOrDefaultAsync(x => x.Data.Key == apiKey, cancellationToken);

            return entity?.Data;
        }
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entity = await session.Query<RavenEntity<ApiToken>>().SingleOrDefaultAsync(x => x.Data.Name == name, cancellationToken);

            return entity?.Data;
        }
    }
}
