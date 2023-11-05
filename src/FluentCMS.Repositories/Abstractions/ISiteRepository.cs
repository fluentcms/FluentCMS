using FluentCMS.Entities.Sites;

namespace FluentCMS.Repositories.Abstractions;

public interface ISiteRepository : IGenericRepository<Site>
{
    Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<bool> CheckUrls(IList<string> urls, CancellationToken cancellationToken = default);
}
