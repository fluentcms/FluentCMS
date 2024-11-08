namespace FluentCMS.Repositories.EFCore;

public class ApiTokenRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ApiToken>(dbContext, apiExecutionContext), IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        return await DbContext.ApiTokens.SingleOrDefaultAsync(x => x.Key == apiKey, cancellationToken);
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        return await DbContext.ApiTokens.FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
    }
}
