namespace FluentCMS.Api.Core.Repositories.Abstractions;

public interface ISiteRepository : IRepository<Site>
{
    Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default);
}
