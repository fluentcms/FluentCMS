using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace FluentCMS.Web.Api;

public class AuthContext : IAuthContext
{
    private readonly string _username = string.Empty;
    private readonly Guid _userId = Guid.Empty;
    private readonly bool _isAuthenticated = false;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider? _serviceProvider;
    private User? _user = null;
    private List<Role>? _roles = [];
    private bool _isUserLoaded = false;
    private bool _areRolesLoaded = false;
    private readonly bool _isApi = false;
    private bool _isApiTokenLoaded = false;
    private ApiToken? _apiToken = null;

    public AuthContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _serviceProvider = _httpContextAccessor.HttpContext?.RequestServices;

        var user = httpContextAccessor.HttpContext?.User;

        if (user != null)
        {
            var idClaimValue = user.FindFirstValue(ClaimTypes.Sid);

            _userId = idClaimValue == null ? Guid.Empty : Guid.Parse(idClaimValue);
            _username = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            _isAuthenticated = user?.Identity?.IsAuthenticated ?? false;
            _isApi = user?.FindFirstValue(ClaimTypes.Actor) == "m2m";
        }
    }

    public string Username => _username;
    public bool IsAuthenticated => _isAuthenticated;

    public bool IsApi => _isApi;

    public Guid UserId => _userId;

    public async Task<ApiToken?> GetApiToken(CancellationToken cancellationToken = default)
    {
        if (_isApiTokenLoaded || !_isAuthenticated)
            return _apiToken;

        var apiTokenService = _serviceProvider?.GetRequiredService<IApiTokenService>();

        if (apiTokenService != null)
            _apiToken = await apiTokenService.GetById(_userId);

        _isApiTokenLoaded = true;

        return _apiToken;
    }

    public async Task<List<Role>> GetRoles(CancellationToken cancellationToken = default)
    {
        if (_areRolesLoaded || !_isAuthenticated)
            return _roles ?? [];


        var roleService = _serviceProvider?.GetRequiredService<IRoleService>();
        var user = await GetUser(cancellationToken);

        if (user == null || roleService == null)
            _roles = [];
        else
            _roles = (await roleService.GetByIds(user.RoleIds ?? [], cancellationToken)).ToList() ?? [];

        _areRolesLoaded = true;

        return _roles;
    }

    public async Task<User?> GetUser(CancellationToken cancellationToken = default)
    {
        if (_isUserLoaded || !_isAuthenticated)
            return _user;

        var userService = _serviceProvider?.GetRequiredService<IUserService>();

        if (userService != null)
            _user = await userService.GetById(UserId, cancellationToken);

        _isUserLoaded = true;

        return _user;
    }
}
