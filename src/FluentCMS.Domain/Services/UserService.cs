using Ardalis.GuardClauses;
using FluentCMS.Entities.Users;
using FluentCMS.Repository;

namespace FluentCMS.Services;

public class UserService
{
    private readonly IUserRepository _userRepository;
    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
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
        return user!;
    }

    public async Task<User> GetByUsername(string username)
    {
        var user = await _userRepository.GetByUsername(username)
            ?? throw new ApplicationException("Requested user does not exists.");
        return user!;
    }

    public async Task Create(User user, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(user);
        Guard.Against.NullOrWhiteSpace(user.Name);
        Guard.Against.NullOrWhiteSpace(user.Username);

        await _userRepository.Create(user, cancellationToken);
    }

    public async Task Update(User user, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(user);
        Guard.Against.Default(user.Id);
        Guard.Against.NullOrWhiteSpace(user.Name);
        Guard.Against.NullOrWhiteSpace(user.Username);

        var oldUser = await _userRepository.GetById(user.Id);
        if (oldUser == null)
            throw new ApplicationException("Provided UserId does not exists.");

        await _userRepository.Update(user, cancellationToken);
    }

    public async Task Delete(User user, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(user);
        Guard.Against.Default(user.Id);

        var oldUser = await _userRepository.GetById(user.Id);
        if (oldUser == null)
            throw new ApplicationException("Provided UserId does not exists.");

        await _userRepository.Delete(user.Id, cancellationToken);
    }
}
