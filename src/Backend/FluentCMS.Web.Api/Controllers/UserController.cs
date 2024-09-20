namespace FluentCMS.Web.Api.Controllers;

public class UserController(IUserService userService, IGlobalSettingsService globalSettingsService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "User Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<UserDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var users = (await userService.GetAll(cancellationToken)).ToList();
        var usersResponse = mapper.Map<List<UserDetailResponse>>(users);

        // setting super admin flag
        var globalSettings = await globalSettingsService.Get(cancellationToken);
        if (globalSettings != null)
            usersResponse.ForEach(user => user.IsSuperAdmin = globalSettings.SuperAdmins.Contains(user.Username));

        return OkPaged(usersResponse);
    }

    [HttpGet("{userId}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<UserDetailResponse>> Get([FromRoute] Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(userId, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(user);

        return Ok(userResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<UserDetailResponse>> Update([FromBody] UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var updated = await userService.Update(user, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(updated);

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

        return Ok(userResponse);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await userService.Delete(id, cancellationToken);
        return Ok(true);
    }
}
