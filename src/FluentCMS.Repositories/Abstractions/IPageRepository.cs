using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IPageRepository : IGenericRepository<Page>
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default);
}
