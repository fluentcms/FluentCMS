using Microsoft.AspNetCore.Http;

namespace FluentCMS.Api;

/// <summary>
/// Represents the application context for an API request, providing user and role information.
/// </summary>
public class ApiApplicationContext : IApplicationContext
{
    private string _username;
    private readonly IEnumerable<Guid> _roleIds;
    private bool _isSuperAdmin;
    private bool _isAuthenticated;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiApplicationContext"/> class using the specified HTTP context accessor.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor to access the current request context.</param>
    public ApiApplicationContext(IHttpContextAccessor httpContextAccessor)
    {
        _username = httpContextAccessor.HttpContext?.User?.Identity?.Name ?? string.Empty;
        _roleIds = httpContextAccessor.HttpContext?.User?.Claims?.Where(x => x.Type == "role")?.Select(x => Guid.Parse(x.Value)) ?? Enumerable.Empty<Guid>();
        _isSuperAdmin = httpContextAccessor.HttpContext?.User?.Claims?.Where(x => x.Type == "IsSuperAdmin")?.Select(x => bool.Parse(x.Value)).SingleOrDefault() ?? false;
        _isAuthenticated = httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    }

    // TODO: This is a temporary workaround to set the super admin user into the context.
    // It should be replaced with a proper authentication system in the future.
    /// <summary>
    /// Sets the specified username as a super admin user in the application context.
    /// </summary>
    /// <param name="username">The username to be set as super admin.</param>
    public void SetSuperAdmin(string username)
    {
        _username = username;
        _isSuperAdmin = true;
        _isAuthenticated = true;
    }

    /// <summary>
    /// Gets the role IDs associated with the current user.
    /// </summary>
    public IEnumerable<Guid> RoleIds => _roleIds;

    /// <summary>
    /// Gets the username of the current user.
    /// </summary>
    public string Username => _username;

    /// <summary>
    /// Gets a value indicating whether the current user is authenticated.
    /// </summary>
    public bool IsAuthenticated => _isAuthenticated;

    /// <summary>
    /// Gets a value indicating whether the current user is a super admin.
    /// </summary>
    public bool IsSuperAdmin => _isSuperAdmin;
}
