namespace FluentCMS.Api.Core.Repositories.Abstractions;

public interface IApiTokenRepository : IRepository<ApiToken>
{
    Task<ApiToken?> GetByKey(string apiKey, CancellationToken cancellationToken);
    Task<ApiToken?> GetByName(string name, CancellationToken cancellationToken);
}
