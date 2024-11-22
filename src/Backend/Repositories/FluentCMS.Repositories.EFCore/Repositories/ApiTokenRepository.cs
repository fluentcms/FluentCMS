namespace FluentCMS.Repositories.EFCore;

public class ApiTokenRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ApiToken, ApiTokenModel>(dbContext, mapper, apiExecutionContext), IApiTokenRepository
{
    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken)
    {
        var dbEntity = await DbContext.ApiTokens
            .Include(x => x.Policies)
            .FirstOrDefaultAsync(x => x.Key == apiKey, cancellationToken);

        return Mapper.Map<ApiToken>(dbEntity);

    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken)
    {
        var dbEntity = await DbContext.ApiTokens
            .Include(x => x.Policies)
            .FirstOrDefaultAsync(x => x.Name == name, cancellationToken);

        return Mapper.Map<ApiToken>(dbEntity);
    }
}
