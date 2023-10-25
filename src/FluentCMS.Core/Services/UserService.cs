using FluentCMS.Dtos.Users;
using FluentCMS.Entities;
using FluentCMS.Repository;

namespace FluentCMS.Services;

public class UserService
{
    private readonly IUserRepository _userRepository;
    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<IEnumerable<UserDto>> GetAll()
    {
        var users = await _userRepository.GetAll();
        return users.Select(x => mapUserToUserDto(x));
    }

    public async Task<UserDto> GetById(Guid id)
    {
        var user = await _userRepository.GetById(id);
        if (user == null)
            throw new ApplicationException("Requested user does not exists.");

        return mapUserToUserDto(user);
    }

    public async Task<UserDto> GetByUsername(string username)
    {
        var user = await _userRepository.GetByUsername(username);
        if (user == null)
            throw new ApplicationException("Requested user does not exists.");

        return mapUserToUserDto(user);
    }

    public Task Create(User user, CancellationToken cancellationToken = default)
    {
        if (user == null)
            throw new ApplicationException("User parameter is not provided.");
        if (string.IsNullOrWhiteSpace(user.Username))
            throw new ApplicationException("Username parameter is not provided.");
        if (string.IsNullOrWhiteSpace(user.Name))
            throw new ApplicationException("Name parameter is not provided.");

        return _userRepository.Create(user, cancellationToken);
    }

    public Task Update(User user, CancellationToken cancellationToken = default)
    {
        if (user == null)
            throw new ApplicationException("User parameter is not provided.");
        if (user.Id == Guid.Empty)
            throw new ApplicationException("Id parameter is not provided.");
        if (string.IsNullOrWhiteSpace(user.Username))
            throw new ApplicationException("Username parameter is not provided.");
        if (string.IsNullOrWhiteSpace(user.Name))
            throw new ApplicationException("Name parameter is not provided.");

        var oldUser = _userRepository.GetById(user.Id);
        if (oldUser == null)
            throw new ApplicationException("Provided UserId does not exists.");

        return _userRepository.Update(user, cancellationToken);
    }

    public Task Delete(User user, CancellationToken cancellationToken = default)
    {
        if (user == null)
            throw new ApplicationException("User parameter is not provided.");
        if (user.Id == Guid.Empty)
            throw new ApplicationException("Id parameter is not provided.");

        var oldUser = _userRepository.GetById(user.Id);
        if (oldUser == null)
            throw new ApplicationException("Provided UserId does not exists.");

        return _userRepository.Delete(user.Id, cancellationToken);
    }

    private UserDto mapUserToUserDto(User user) => new UserDto
    {
        Id = user.Id,
        Name = user.Name,
        Username = user.Username,
        Password = user.Password,
    };
}
