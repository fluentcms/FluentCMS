using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IRoleRepository : IGenericRepository<Role>
{
    Task<IEnumerable<Role>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
}
