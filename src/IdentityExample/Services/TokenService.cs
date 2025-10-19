using IdentityExample.Configuration;
using IdentityExample.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IdentityExample.Services;

public class TokenService(AppIdentityDbContext context, IOptions<JwtSettings> jwtSettings) : ITokenService
{
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

    public async Task<string> GenerateTokenAsync(User user, string ipAddress, string deviceInfo)
    {
        var tokenId = Guid.NewGuid();
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_jwtSettings.ExpirationMinutes);

        // Create JWT claims as specified in documentation
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, tokenId.ToString()),
            new(JwtRegisteredClaimNames.Iat, ((DateTimeOffset)issuedAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Iss, _jwtSettings.Issuer),
            new(JwtRegisteredClaimNames.Aud, _jwtSettings.Audience)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Store token in database for tracking
        var jwtToken = new JwtToken
        {
            Id = tokenId,
            UserId = user.Id,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            IsRevoked = false
        };

        context.JwtTokens.Add(jwtToken);
        await context.SaveChangesAsync();

        return tokenString;
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);

            // Validate JWT signature and expiration
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var tokenId = Guid.Parse(jwtToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Jti).Value);

            // Check if token exists in database and is not revoked
            var dbToken = await context.JwtTokens
                .FirstOrDefaultAsync(t => t.Id == tokenId && !t.IsRevoked);

            return dbToken != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task RevokeTokenAsync(Guid tokenId)
    {
        var token = await context.JwtTokens.FindAsync(tokenId);
        if (token != null)
        {
            token.IsRevoked = true;
            await context.SaveChangesAsync();
        }
    }

    public async Task RevokeAllUserTokensAsync(Guid userId)
    {
        var tokens = await context.JwtTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
        }

        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<JwtToken>> GetActiveTokensAsync()
    {
        return await context.JwtTokens
            .Include(t => t.User)
            .Where(t => !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.IssuedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<JwtToken>> GetUserActiveTokensAsync(Guid userId)
    {
        return await context.JwtTokens
            .Where(t => t.UserId == userId && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.IssuedAt)
            .ToListAsync();
    }

    public async Task CleanupExpiredTokensAsync()
    {
        var cutoffDate = DateTime.UtcNow;
        var expiredTokens = await context.JwtTokens
            .Where(t => t.ExpiresAt <= cutoffDate)
            .ToListAsync();

        context.JwtTokens.RemoveRange(expiredTokens);
        await context.SaveChangesAsync();
    }
}
