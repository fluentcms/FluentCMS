namespace FluentCMS.Repositories.Abstractions;

public interface IApiTokenRepository : IAuditableEntityRepository<ApiToken>
{
    Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken);
    Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken);
}
