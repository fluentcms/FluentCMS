using FluentCMS.Entities.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace FluentCMS.Api.Identity;
public class FluentSignInManager : SignInManager<User>
{
    private readonly IConfiguration _configuration;

    public FluentSignInManager(FluentUserManager userManager, IHttpContextAccessor contextAccessor, IUserClaimsPrincipalFactory<User> claimsFactory, IOptions<IdentityOptions> optionsAccessor, ILogger<FluentSignInManager> logger, IAuthenticationSchemeProvider schemes, IUserConfirmation<User> confirmation, IConfiguration configuration
        ) : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _configuration = configuration;
    }

    internal string CreateJwtToken(ClaimsPrincipal claimsPrincipal)
    {
        var key = _configuration["Jwt:Key"] ?? "no-key";
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var algorithm = SecurityAlgorithms.HmacSha256;
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddMinutes(5), claims: claimsPrincipal.Claims,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(keyBytes), algorithm));
        return tokenHandler.WriteToken(token);
    }
}
