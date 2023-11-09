using FluentCMS.Entities.Identity;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.Identity.Abstractions;

public interface IRoleRepository : IGenericRepository<Role>, IQueryableRepository<Role>
{
    Task<Role?> FindByName(string normalizedRoleName, CancellationToken cancellationToken);
}