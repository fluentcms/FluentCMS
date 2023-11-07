using FluentCMS.Entities.Users;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IUserService
{
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<User> GetByUsername(string username, CancellationToken cancellationToken = default);
    Task<User> Create(
        string name, string username, string password, IEnumerable<Guid> roles,
        CancellationToken cancellationToken = default);
    Task<User> Edit(Guid id,
        string name, string username, string password, IEnumerable<Guid> roles,
        CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
}

internal class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default)
    {
        var users = await _userRepository.GetAll(cancellationToken);
        return users;
    }

    public async Task<User> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetById(id, cancellationToken)
            ?? throw AppException.BadRequest("Requested user does not exists.");
        return user;
    }

    public async Task<User> GetByUsername(string username, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByUsername(username, cancellationToken)
            ?? throw AppException.BadRequest("Requested user does not exists.");
        return user;
    }

    public async Task<User> Create(
        string name, string username, string password, IEnumerable<Guid> roles,
        CancellationToken cancellationToken = default)
    {
        // check for duplicate username
        var duplicateUser = await _userRepository.GetByUsername(username, cancellationToken);
        if (duplicateUser != null)
            throw AppException.BadRequest("duplicate user exists");

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Name = name,
            Username = username,
            Password = password,
            UserRoles = roles?.Select(x => new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = x
            }).ToList() ?? []
        };

        var newUser = await _userRepository.Create(user, cancellationToken);
        return newUser ?? throw new Exception("User not created");
    }

    public async Task<User> Edit(Guid id,
        string name, string username, string password, IEnumerable<Guid> roles,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetById(id, cancellationToken);
        if (user == null)
            throw AppException.BadRequest("user is not exists.");

        // check for duplicate username
        if (user.Username != username)
        {
            var duplicateUser = await _userRepository.GetByUsername(user.Username, cancellationToken);
            if (duplicateUser != null)
                throw AppException.BadRequest("duplicate username exists");
        }

        user.Name = name;
        user.Username = username;
        user.Password = string.IsNullOrWhiteSpace(password) ? user.Password : password;
        user.UserRoles = roles?.Select(x => new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = x
        }).ToList() ?? [];

        var updatedUser = await _userRepository.Update(user, cancellationToken);
        return updatedUser ?? throw new Exception("User not updated.");
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetById(id, cancellationToken);
        if (user == null)
            throw AppException.BadRequest("user is not exists.");

        var deletedUser = await _userRepository.Delete(user.Id, cancellationToken);
        if (deletedUser is null) throw new Exception("User not deleted.");
    }
}
