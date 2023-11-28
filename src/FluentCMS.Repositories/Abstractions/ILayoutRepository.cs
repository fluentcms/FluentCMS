using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface ILayoutRepository : IGenericRepository<Layout>
{
    Task<IEnumerable<Layout>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
}
