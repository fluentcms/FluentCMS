using FluentCMS.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Providers.Identity;

public interface IUserTokenProvider
{
    Task<UserToken> Generate(User user);
    Task<Guid> Validate(string accessToken);
    Task<Guid> ValidateExpiredToken(string accessToken);
}

public class JwtUserTokenProvider : IUserTokenProvider
{
    private readonly JwtOptions _options;

    public JwtUserTokenProvider(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public virtual async Task<UserToken> Generate(User user)
    {
        return await Task.Run(() =>
        {
            var token = GenerateToken(user);
            var refreshToken = GenerateRefreshToken(user);
            return new UserToken
            {
                AccessToken = token.AccessToken,
                Expiry = token.Expiry,
                RefreshToken = refreshToken
            };
        });
    }

    private string GenerateRefreshToken(User user)
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private UserToken GenerateToken(User user)
    {
        var jwtTokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_options.Secret);
        var claims = GetJwtClaims(user);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddSeconds(_options.TokenExpiry),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature),
            NotBefore = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow
        };

        var token = jwtTokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        return new UserToken
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            Expiry = tokenDescriptor.Expires.Value
        };
    }

    private List<Claim> GetJwtClaims(User user)
    {
        var userId = user.Id.ToString();
        var result = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTime.Now.ToLongDateString()),
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, user.UserName)
        };
        if (!string.IsNullOrWhiteSpace(user.NormalizedEmail)) result.Add(new Claim(ClaimTypes.Email, user.NormalizedEmail));
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber)) result.Add(new Claim(ClaimTypes.Email, user.PhoneNumber));
        result.AddRange(user.RoleIds.Select(userRole => new Claim(ClaimTypes.Role, userRole.ToString())));
        return result;
    }

    public async Task<Guid> Validate(string accessToken)
    {
        return await Task.Run(() =>
        {
            var jwtTokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_options.Secret);
            var principal = jwtTokenHandler.ValidateToken(accessToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                SaveSigninToken = true,
                ValidAudience = _options.Audience,
                ValidIssuer = _options.Issuer
            }, out _);

            var userId = GetUserId(principal);

            return userId;
        });
    }

    public async Task<Guid> ValidateExpiredToken(string accessToken)
    {
        return await Task.Run(() =>
        {
            var jwtTokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_options.Secret);
            var principal = jwtTokenHandler.ValidateToken(accessToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                RequireExpirationTime = true,
                ValidateLifetime = false,
                SaveSigninToken = true,
                ValidAudience = _options.Audience,
                ValidIssuer = _options.Issuer
            }, out _);

            var userId = GetUserId(principal);

            return userId;
        });
    }

    private Guid GetUserId(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                     principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (userId is null) throw new Exception("Token is not valid.");

        return Guid.Parse(userId);
    }
}

public class UserToken
{
    public required string AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime Expiry { get; set; }
}

public class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public double TokenExpiry { get; set; } = -1; // second, -1 means never expire
    public double RefreshTokenExpiry { get; set; } = -1; // second, -1 means never expire
    public string Secret { get; set; } = string.Empty;
}
