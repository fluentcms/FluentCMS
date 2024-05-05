using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.Services;

public interface IApiTokenProvider
{
    public string GenerateToken(ApiToken apiToken);

}

public class JwtApiTokenProvider(IOptions<JwtOptions> options) : IApiTokenProvider
{
    public string GenerateToken(ApiToken apiToken)
    {
        var jwtTokenHandler = new JwtSecurityTokenHandler();
        var key = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(options.Value.Secret));
        var claims = GetJwtClaims(apiToken);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = apiToken.ExpiredAt ?? DateTime.MaxValue,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature),
            NotBefore = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow
        };

        var token = jwtTokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        return jwtTokenHandler.WriteToken(token);
    }

    private IEnumerable<Claim>? GetJwtClaims(ApiToken apiToken)
    {
        yield return new Claim(ClaimTypes.Sid, apiToken.Id.ToString("D"));
        yield return new Claim(ClaimTypes.NameIdentifier, $"api-{apiToken.Id:D}");
        yield return new Claim(ClaimTypes.Actor, "m2m");
    }
}
