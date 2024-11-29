using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FluentCMS.Services;

// TODO: think about this implementation, it may need a full refactor!
public interface IUserTokenProvider
{
    Task<UserToken> Generate(User user);
    Task<Guid> Validate(string accessToken);
    Task<Guid> ValidateExpiredToken(string accessToken);
}

public class JwtUserTokenProvider(IOptions<JwtOptions> options) : IUserTokenProvider
{
    private readonly JwtOptions _options = options.Value;

    public virtual async Task<UserToken> Generate(User user)
    {
        return await Task.Run(() =>
        {
            var token = GenerateToken(user);
            var refreshToken = GenerateRefreshToken();
            return new UserToken
            {
                AccessToken = token.AccessToken,
                Expiry = token.Expiry,
                RefreshToken = refreshToken
            };
        });
    }

    private static string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private UserToken GenerateToken(User user)
    {
        var jwtTokenHandler = new JwtSecurityTokenHandler();
        var key = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(_options.Secret));
        var claims = GetJwtClaims(user);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            //todo : Fix this
            Expires = _options.TokenExpiry == -1 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(_options.TokenExpiry),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature),
            NotBefore = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow
        };

        var token = jwtTokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        return new UserToken
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            Expiry = tokenDescriptor.Expires ?? DateTime.MaxValue
        };
    }

    private List<Claim> GetJwtClaims(User user)
    {
        var userId = user.Id.ToString();
        var result = new List<Claim>
        {
            new(ClaimTypes.Sid, userId),
            new(ClaimTypes.NameIdentifier, user.UserName?? string.Empty),
        };
        if (!string.IsNullOrWhiteSpace(user.NormalizedEmail))
            result.Add(new Claim(ClaimTypes.Email, user.NormalizedEmail));

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            result.Add(new Claim(ClaimTypes.Email, user.PhoneNumber));

        return result;
    }

    public async Task<Guid> Validate(string accessToken)
    {
        return await Task.Run(() =>
        {
            var jwtTokenHandler = new JwtSecurityTokenHandler();
            var key = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(_options.Secret));
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
            var key = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(_options.Secret));
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

