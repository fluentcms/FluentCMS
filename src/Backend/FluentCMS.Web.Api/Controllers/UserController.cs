namespace FluentCMS.Web.Api.Controllers;

public class UserController(IUserService userService, IRoleService roleService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "User Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<UserDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var users = (await userService.GetAll(cancellationToken)).ToList();
        var usersResponse = mapper.Map<List<UserDetailResponse>>(users);
        var roles = await roleService.GetAll(cancellationToken);

        for (int i = 0; i < users.Count; i++)
        {
            var userRoles = roles.Where(r => users[i].RoleIds.Contains(r.Id)).ToList();
            usersResponse[i].Roles = mapper.Map<List<RoleDetailResponse>>(userRoles);
        }

        return OkPaged(usersResponse);
    }

    [HttpGet("{userId}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<UserDetailResponse>> Get([FromRoute] Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(userId, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(user);
        userResponse.Roles = await GetUsersRoles(user, cancellationToken);

        return Ok(userResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<UserDetailResponse>> Update([FromBody] UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var updated = await userService.Update(user, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(updated);
        userResponse.Roles = await GetUsersRoles(user, cancellationToken);

        return Ok(userResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<bool>> SetPassword([FromBody] UserSetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(request.UserId, cancellationToken);
        await userService.ChangePassword(user, request.Password, cancellationToken);
        return Ok(true);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<UserDetailResponse>> Create([FromBody] UserCreateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var created = await userService.Create(user, request.Password, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(created);
        userResponse.Roles = await GetUsersRoles(user, cancellationToken);

        return Ok(userResponse);
    }

    private async Task<List<RoleDetailResponse>> GetUsersRoles(User user, CancellationToken cancellationToken = default)
    {
        var roles = await roleService.GetAll(cancellationToken);
        var userRoles = roles.Where(r => user.RoleIds.Contains(r.Id)).ToList();
        return mapper.Map<List<RoleDetailResponse>>(userRoles);
    }
}
