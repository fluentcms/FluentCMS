namespace FluentCMS.Api.Plugins.IdentityManagement.Controllers;

public class UsersController(IUserService userService) : BaseController
{
    [HttpGet]
    public async Task<ApiListResponse<UserDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var users = await userService.GetAll(cancellationToken);
        var usersDto = Mapper.Map<List<UserDto>>(users);
        return SuccessList(usersDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<UserDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(id, cancellationToken);
        return Success(Mapper.Map<UserDto>(user));
    }

    [HttpPost]
    public async Task<ApiResponse<UserDto>> Add(UserAddRequest request, CancellationToken cancellationToken = default)
    {
        var user = Mapper.Map<User>(request);
        await userService.Add(user, request.Password, cancellationToken);
        return Success(Mapper.Map<UserDto>(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<UserDto>> Update(Guid id, UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(id, cancellationToken);
        Mapper.Map(request, user);
        await userService.Update(user, cancellationToken);
        return Success(Mapper.Map<UserDto>(user));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        await userService.Remove(id, cancellationToken);
        return Success();
    }

}
