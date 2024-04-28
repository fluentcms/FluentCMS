using FluentCMS.Web.Api.Attributes;

namespace FluentCMS.Web.Api.Controllers;

public class UserController(IUserService userService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "User Management";
    public const string READ = "Read";
    public const string UPDATE = "Update";
    public const string CREATE = "Create";

    [HttpGet]
    [AuthorizePolicy(AREA, READ)]
    public async Task<IApiPagingResult<UserDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var users = await userService.GetAll(cancellationToken);
        var usersResponse = mapper.Map<List<UserDetailResponse>>(users);
        return OkPaged(usersResponse);
    }

    [HttpGet("{userId}")]
    [AuthorizePolicy(AREA, READ)]
    public async Task<IApiResult<UserDetailResponse>> Get([FromRoute] Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(userId, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(user);
        return Ok(userResponse);
    }

    [HttpPut]
    [AuthorizePolicy(AREA, UPDATE)]
    public async Task<IApiResult<UserDetailResponse>> Update([FromBody] UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var updated = await userService.Update(user, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(updated);
        return Ok(userResponse);
    }

    [HttpPost]
    [AuthorizePolicy(AREA, CREATE)]
    public async Task<IApiResult<UserDetailResponse>> Create([FromBody] UserCreateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var created = await userService.Create(user, request.Password, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(created);
        return Ok(userResponse);
    }
}
