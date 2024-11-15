
namespace FluentCMS.Repositories.EFCore;

public class ApiTokenRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ApiToken>(dbContext, apiExecutionContext), IApiTokenRepository
{
    public override async Task<ApiToken?> Create(ApiToken entity, CancellationToken cancellationToken = default)
    {
        var apiToken = await base.Create(entity, cancellationToken);

        var policyValues = entity.Policies.Where(x => x.Actions.Count != 0).Select(x =>
        new PolicyValue
        {
            Id = Guid.NewGuid(),
            ApiTokenId = entity.Id,
            Area = x.Area,
            Actions = x.Actions
        }).ToList();

        await DbContext.ApiTokenPolicies.AddRangeAsync(policyValues, cancellationToken);

        return apiToken;
    }

    public override async Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default)
    {
        var apiTokens = await base.GetAll(cancellationToken);
        var apiTokenPolicies = await DbContext.ApiTokenPolicies.ToListAsync(cancellationToken);

        foreach (var apiToken in apiTokens)
        {
            apiToken.Policies = apiTokenPolicies.Where(x => x.ApiTokenId == apiToken.Id).Select(x =>
            new Policy
            {
                Area = x.Area,
                Actions = x.Actions
            }).ToList();
        }

        return apiTokens;
    }

    public async Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken = default)
    {
        var apiToken = await DbContext.ApiTokens.SingleOrDefaultAsync(x => x.Key == apiKey, cancellationToken);
        return apiToken;
    }

    public async Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        var apiToken = await DbContext.ApiTokens.FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
        return apiToken;
    }
}
