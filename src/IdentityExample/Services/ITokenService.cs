using IdentityExample.Models;

namespace IdentityExample.Services;

public interface ITokenService
{
    Task<string> GenerateTokenAsync(User user, string ipAddress, string deviceInfo);
    Task<bool> ValidateTokenAsync(string token);
    Task RevokeTokenAsync(Guid tokenId);
    Task RevokeAllUserTokensAsync(Guid userId);
    Task<IEnumerable<JwtToken>> GetActiveTokensAsync();
    Task<IEnumerable<JwtToken>> GetUserActiveTokensAsync(Guid userId);
    Task CleanupExpiredTokensAsync();
}
