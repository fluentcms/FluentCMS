namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class ApiTokenRepository(CmsCoreDbContext dbContext) : RepositoryBase<ApiToken>(dbContext), IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken)
    {
        return await Query().FirstOrDefault(x => x.Key == apiKey, cancellationToken);
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken)
    {
        return await Query().FirstOrDefault(x => x.Name == name, cancellationToken);
    }
}
