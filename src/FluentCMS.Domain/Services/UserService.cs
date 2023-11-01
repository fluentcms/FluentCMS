using Ardalis.GuardClauses;
using FluentCMS.Entities.Users;
using FluentCMS.Repository;

namespace FluentCMS.Services;

public class UserService
{
    private readonly IUserRepository _userRepository;
    private readonly IGenericRepository<UserRole> _userRoleRepository;
    public UserService(
        IUserRepository userRepository,
        IGenericRepository<UserRole> userRoleRepository)
    {
        _userRepository = userRepository;
        _userRoleRepository = userRoleRepository;
    }

    public async Task<IEnumerable<User>> GetAll()
    {
        var users = await _userRepository.GetAll();
        return users;
    }

    public async Task<User> GetById(Guid id)
    {
        var user = await _userRepository.GetById(id)
            ?? throw new ApplicationException("Requested user does not exists.");
        return user;
    }

    public async Task<User> GetByUsername(string username)
    {
        var user = await _userRepository.GetByUsername(username)
            ?? throw new ApplicationException("Requested user does not exists.");
        return user;
    }

    public async Task Create(User user, IEnumerable<Guid> roles, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(user);
        Guard.Against.NullOrWhiteSpace(user.Name);
        Guard.Against.NullOrWhiteSpace(user.Username);

        await _userRepository.Create(user, cancellationToken);

        if (roles != null && roles.Any())
        {
            await AddUserRoles(user.Id, roles);
        }
    }

    public async Task Update(User user, IEnumerable<Guid> roles, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(user);
        Guard.Against.Default(user.Id);
        Guard.Against.NullOrWhiteSpace(user.Name);
        Guard.Against.NullOrWhiteSpace(user.Username);

        var oldUser = await _userRepository.GetById(user.Id);
        if (oldUser == null)
            throw new ApplicationException("Provided UserId does not exists.");

        await _userRepository.Update(user, cancellationToken);

        // update user-roles
        await RemoveUserRoles(user.Id);
        if (roles != null && roles.Any())
        {
            await AddUserRoles(user.Id, roles);
        }
    }

    public async Task Delete(User user, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(user);
        Guard.Against.Default(user.Id);

        var oldUser = await _userRepository.GetById(user.Id);
        if (oldUser == null)
            throw new ApplicationException("Provided UserId does not exists.");

        await _userRepository.Delete(user.Id, cancellationToken);

        // remove user-roles
        await RemoveUserRoles(user.Id);
    }

    public async Task AddUserRoles(Guid userId, IEnumerable<Guid> roles)
    {
        Guard.Against.Default(userId);
        Guard.Against.NullOrEmpty(roles);

        var roleModels = new List<UserRole>();
        foreach (var roleId in roles)
        {
            var role = new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = roleId,
            };
            roleModels.Add(role);
        }
        await _userRoleRepository.CreateMany(roleModels);
    }

    public async Task RemoveUserRoles(Guid userId)
    {
        var userRoles = await _userRoleRepository.GetAll(x => x.UserId == userId);
        foreach (var role in userRoles)
        {
            await _userRoleRepository.Delete(role.Id);
        }
    }
}
