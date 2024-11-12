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

    public async override Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default)
    {
        return await DbContext.ApiTokens.Include(x => x.Policies).ToListAsync(cancellationToken);
    }

    public async override Task<ApiToken?> GetById(Guid tokenId, CancellationToken cancellationToken = default)
    {
        return await DbContext.ApiTokens.Include(x => x.Policies).Where(entity => tokenId == entity.Id).SingleOrDefaultAsync(cancellationToken);
    }

    public async override Task<IEnumerable<ApiToken>> GetByIds(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        return await DbContext.ApiTokens.Include(x=> x.Policies).Where(entity => ids.Contains(entity.Id)).ToListAsync(cancellationToken);
    }
}
