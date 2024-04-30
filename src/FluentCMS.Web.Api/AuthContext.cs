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
        }
    }

    public string Username => _username;
    public bool IsAuthenticated => _isAuthenticated;
    public Guid UserId => _userId;

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
