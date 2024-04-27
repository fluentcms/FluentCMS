using FluentCMS.Web.Api.Attributes;
using System.Reflection;

namespace FluentCMS.Web.Api.Controllers;

public class UserController(IUserService userService, IMapper mapper) : BaseGlobalController
{

    [HttpGet]
    [DynamicAuthorize("User Management", "Read")]
    public async Task<IApiPagingResult<UserDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var users = await userService.GetAll(cancellationToken);
        var usersResponse = mapper.Map<List<UserDetailResponse>>(users);
        return OkPaged(usersResponse);
    }

    [HttpGet("{userId}")]
    [DynamicAuthorize("User Management", "Read")]
    public async Task<IApiResult<UserDetailResponse>> Get([FromRoute] Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userService.GetById(userId, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(user);
        return Ok(userResponse);
    }

    [HttpPut]
    [DynamicAuthorize("User Management", "Update")]
    public async Task<IApiResult<UserDetailResponse>> Update([FromBody] UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var updated = await userService.Update(user, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(updated);
        return Ok(userResponse);
    }

    [HttpPost]
    [DynamicAuthorize("User Management", "Create")]
    public async Task<IApiResult<UserDetailResponse>> Create([FromBody] UserCreateRequest request, CancellationToken cancellationToken = default)
    {
        var user = mapper.Map<User>(request);
        var created = await userService.Create(user, request.Password, cancellationToken);
        var userResponse = mapper.Map<UserDetailResponse>(created);
        return Ok(userResponse);
    }
}
