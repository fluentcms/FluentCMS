using System.Security.Claims;

namespace FluentCMS.Repositories.Decorators;

public class UserRepositoryDecorator : EntityRepositoryDecorator<User>, IUserRepository
{
    private readonly IUserRepository _decorator;

    public UserRepositoryDecorator(IAuthContext authContext, IUserRepository decorator) : base(authContext, decorator)
    {
        _decorator = decorator;
    }

    public IQueryable<User> AsQueryable()
    {
        return _decorator.AsQueryable();
    }

    public Task<User?> FindByEmail(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        return _decorator.FindByEmail(normalizedEmail, cancellationToken);
    }

    public Task<User?> FindByLogin(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        return _decorator.FindByLogin(loginProvider, providerKey, cancellationToken);
    }

    public Task<User?> FindByName(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        return _decorator.FindByName(normalizedUserName, cancellationToken);
    }

    public Task<IList<User>> GetUsersForClaim(Claim claim, CancellationToken cancellationToken = default)
    {
        return _decorator.GetUsersForClaim(claim, cancellationToken);
    }

    public Task<IList<User>> GetUsersInRole(string roleId, CancellationToken cancellationToken = default)
    {
        return _decorator.GetUsersInRole(roleId, cancellationToken);
    }
}
