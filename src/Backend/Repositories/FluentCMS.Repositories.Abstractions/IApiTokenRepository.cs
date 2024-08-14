namespace FluentCMS.Repositories.Abstractions;

public interface IApiTokenRepository : IAuditableEntityRepository<ApiToken>
{
    Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken);
}
