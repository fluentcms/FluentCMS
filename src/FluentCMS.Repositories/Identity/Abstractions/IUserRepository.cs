using FluentCMS.Repositories.Abstractions;
using FluentCMS.Entities.Identity;
using System.Security.Claims;

namespace FluentCMS.Repositories.Identity.Abstractions;

public interface IUserRepository : IGenericRepository<User>, IQueryableRepository<User>
{
    Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken);
    Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken);
    Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken);
    Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken);
    Task<IList<User>> GetUsersInRole(string roleId, CancellationToken cancellationToken);
}