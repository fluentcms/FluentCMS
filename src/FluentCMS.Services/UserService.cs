using FluentCMS.Entities.Users;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IUserService
{
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<User> GetByUsername(string username, CancellationToken cancellationToken = default);
    Task<User> Create(User user, CancellationToken cancellationToken = default);
    Task<User> Edit(User user, CancellationToken cancellationToken = default);
    Task Delete(User user, CancellationToken cancellationToken = default);
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
            ?? throw new ApplicationException("Requested user does not exists.");
        return user;
    }

    public async Task<User> GetByUsername(string username, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByUsername(username, cancellationToken)
            ?? throw new ApplicationException("Requested user does not exists.");
        return user;
    }

    public async Task<User> Create(User user, CancellationToken cancellationToken = default)
    {
        if (user is null)
            throw new ApplicationException("user is not provided");

        var newUser = await _userRepository.Create(user, cancellationToken);
        return newUser ?? throw new ApplicationException("User not created");
    }

    public async Task<User> Edit(User user, CancellationToken cancellationToken = default)
    {
        if (user == null)
            throw new ApplicationException("user is not provided");

        var updatedUser = await _userRepository.Update(user, cancellationToken);
        return updatedUser ?? throw new ApplicationException("User not updated.");
    }

    public async Task Delete(User user, CancellationToken cancellationToken = default)
    {
        if (user == null)
            throw new ApplicationException("user is not provided");

        var deletedUser = await _userRepository.Delete(user.Id, cancellationToken);
        if (deletedUser is null) throw new ApplicationException("User not deleted.");
    }
}
