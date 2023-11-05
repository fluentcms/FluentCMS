using Ardalis.GuardClauses;
using FluentCMS.Application.Dtos.Users;
using FluentCMS.Entities.Users;
using FluentCMS.Repository;

namespace FluentCMS.Application.Services;
public interface IUserService
{
    Task<UserDto> GetById(Guid id);
    Task<UserDto> GetByUsername(string username);
    Task<SearchUserResponse> Search(SearchUserRequest request);
    Task<Guid> Create(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task Edit(EditUserRequest request, CancellationToken cancellationToken = default);
    Task Delete(DeleteUserRequest request, CancellationToken cancellationToken = default);
}

internal class UserService : IUserService
{
    private readonly AutoMapper.IMapper _mapper;
    private readonly IUserRepository _userRepository;
    public UserService(
        AutoMapper.IMapper mapper,
        IUserRepository userRepository)
    {
        _mapper = mapper;
        _userRepository = userRepository;
    }

    public async Task<UserDto> GetById(Guid id)
    {
        var user = await _userRepository.GetById(id)
            ?? throw new ApplicationException("Requested user does not exists.");
        return _mapper.Map<UserDto>(user);
    }

    public async Task<UserDto> GetByUsername(string username)
    {
        var user = await _userRepository.GetByUsername(username)
            ?? throw new ApplicationException("Requested user does not exists.");
        return _mapper.Map<UserDto>(user);
    }

    public async Task<SearchUserResponse> Search(SearchUserRequest request)
    {
        var users = await _userRepository.GetAll(x =>
            string.IsNullOrWhiteSpace(request.Name) || x.Name.Contains(request.Name));
        return new SearchUserResponse
        {
            Data = users.Select(x => _mapper.Map<UserDto>(x)),
            Total = users.Count(),
        };
    }

    public async Task<Guid> Create(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request);
        Guard.Against.NullOrWhiteSpace(request.Name);
        Guard.Against.NullOrWhiteSpace(request.Username);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            CreatedBy = "",
            LastUpdatedBy = "",
            Name = request.Name,
            Username = request.Username,
            Password = request.Password,
            UserRoles = request.Roles?.Select(r => new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = r,
            }).ToList() ?? [],
        };

        await _userRepository.Create(user, cancellationToken);
        return userId;
    }

    public async Task Edit(EditUserRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request);
        Guard.Against.Default(request.Id);
        Guard.Against.NullOrWhiteSpace(request.Name);
        Guard.Against.NullOrWhiteSpace(request.Username);

        var user = await _userRepository.GetById(request.Id);
        if (user == null)
            throw new ApplicationException("Provided UserId does not exists.");

        user.LastUpdatedAt = DateTime.UtcNow;
        user.LastUpdatedBy = "";
        user.Name = request.Name;
        user.Username = request.Username;
        user.Password = request.Password;
        user.UserRoles = request.Roles?.Select(r => new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = r,
        }).ToList() ?? [];

        await _userRepository.Update(user, cancellationToken);
    }

    public async Task Delete(DeleteUserRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request);
        Guard.Against.Default(request.Id);

        var user = await _userRepository.GetById(request.Id);
        if (user == null)
            throw new ApplicationException("Provided UserId does not exists.");

        await _userRepository.Delete(user.Id, cancellationToken);
    }
}
