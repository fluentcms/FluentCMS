using FluentCMS.Entities;

namespace FluentCMS.Repositories.Abstractions;

public interface ISiteRepository : IGenericRepository<Site>
{
    Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default);
}
