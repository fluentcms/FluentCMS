using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface ISiteRepository : ISiteAssociatedRepository<Site>
{
    Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default);
}
