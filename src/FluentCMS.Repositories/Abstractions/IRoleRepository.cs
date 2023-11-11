using FluentCMS.Entities;

namespace FluentCMS.Repositories.Abstractions;

public interface IRoleRepository : IGenericRepository<Role>, IQueryableRepository<Role>
{
    Task<Role?> FindByName(string normalizedRoleName, CancellationToken cancellationToken);
}