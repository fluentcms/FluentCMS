using System.Security.Claims;

namespace FluentCMS.Repositories.Abstractions;

public interface IUserRepository : IAuditableEntityRepository<User>, IQueryableRepository<User>
{
    Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default);
    Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default);
    Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default);
}
