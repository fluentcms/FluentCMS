using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IRoleRepository : IGenericRepository<Role>, IQueryableRepository<Role>
{
    Task<Role?> FindByName(string normalizedRoleName, CancellationToken cancellationToken);
}
