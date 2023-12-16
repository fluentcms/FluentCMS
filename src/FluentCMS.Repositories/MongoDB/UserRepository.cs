using FluentCMS.Entities;
using MongoDB.Driver;
using System.Security.Claims;

namespace FluentCMS.Repositories.MongoDB;

/// <summary>
/// Repository for managing User entities in MongoDB.
/// Extends AuditEntityRepository to include audit functionality.
/// </summary>
public class UserRepository : AuditEntityRepository<User>, IUserRepository
{
    /// <summary>
    /// Initializes a new instance of the UserRepository class.
    /// </summary>
    /// <param name="mongoDbContext">The MongoDB context used to access the database.</param>
    /// <param name="applicationContext">The application context for the repository.</param>
    public UserRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
        : base(mongoDbContext, applicationContext)
    {
    }

    /// <summary>
    /// Provides a queryable interface to the User collection.
    /// </summary>
    /// <returns>An IQueryable of User entities.</returns>
    public IQueryable<User> AsQueryable()
    {
        return Collection.AsQueryable();
    }

    /// <summary>
    /// Retrieves a list of users who have the specified claim.
    /// </summary>
    /// <param name="claim">The claim to filter users by.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns a list of users.</returns>
    public Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = AsQueryable().Where(u => u.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value)).ToList();
        return Task.FromResult((IList<User>)users);
    }

    /// <summary>
    /// Finds a user by their normalized email.
    /// </summary>
    /// <param name="normalizedEmail">The normalized email to search for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the user if found.</returns>
    public Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().SingleOrDefault(x => x.NormalizedEmail == normalizedEmail);
        return Task.FromResult(user);
    }

    /// <summary>
    /// Finds a user by their login provider and provider key.
    /// </summary>
    /// <param name="loginProvider">The login provider.</param>
    /// <param name="providerKey">The provider key.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the user if found.</returns>
    public Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().FirstOrDefault(user => user.Logins.Any(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey));
        return Task.FromResult(user);
    }

    /// <summary>
    /// Finds a user by their normalized username.
    /// </summary>
    /// <param name="normalizedUserName">The normalized username to search for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns the user if found.</returns>
    public Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = AsQueryable().FirstOrDefault(x => x.NormalizedUserName == normalizedUserName);
        return Task.FromResult(user);
    }

    /// <summary>
    /// Retrieves a list of users who are in the specified role.
    /// </summary>
    /// <param name="roleId">The role ID to filter users by.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation and returns a list of users.</returns>
    public Task<IList<User>> GetUsersInRole(string roleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = Collection.AsQueryable().Where(x => x.RoleIds.Any(r => roleId.Equals(r))).ToList();
        return Task.FromResult((IList<User>)users);
    }
}
