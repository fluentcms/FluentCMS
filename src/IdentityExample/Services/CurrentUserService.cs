using Microsoft.AspNetCore.Identity;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace IdentityExample.Services;

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<User> _userManager;
    private User? _user;
    private bool _userLoaded = false;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, UserManager<User> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public Guid? UserId
    {
        get
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.Request.Headers["X-User-Token"]
                .FirstOrDefault();

            if (string.IsNullOrEmpty(userIdClaim))
                return null;

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.ReadJwtToken(userIdClaim);
                var userIdString = token.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub)?.Value;
                
                if (Guid.TryParse(userIdString, out var userId))
                    return userId;
            }
            catch
            {
                // Token parsing failed
            }

            return null;
        }
    }

    public User? User
    {
        get
        {
            if (_userLoaded)
                return _user;

            _userLoaded = true;

            if (UserId.HasValue)
            {
                _user = _userManager.FindByIdAsync(UserId.Value.ToString()).Result;
            }

            return _user;
        }
    }

    public bool IsSuperAdmin => User?.IsSuperAdmin ?? false;

    public bool IsAuthenticated => UserId.HasValue && User != null;
}
