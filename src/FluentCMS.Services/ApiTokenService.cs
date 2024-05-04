using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.Services;

public interface IApiTokenService : IAutoRegisterService
{
    Task<ApiToken> Create(ApiToken apiToken, CancellationToken cancellationToken = default);
    Task<ApiToken> Delete(Guid tokenId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default);
    Task<ApiToken?> GetById(Guid tokenId, CancellationToken cancellationToken = default);
    Task<ApiToken> Update(Guid tokenId, string name, string? description, bool enabled, List<Policy> policies, CancellationToken cancellationToken = default);
}

public class ApiTokenService(IApiTokenRepository apiTokenRepository, IOptions<JwtOptions> options) : IApiTokenService
{
    public async Task<IEnumerable<ApiToken>> GetAll(CancellationToken cancellationToken = default)
    {
        return await apiTokenRepository.GetAll(cancellationToken);
    }

    public async Task<ApiToken?> GetById(Guid tokenId, CancellationToken cancellationToken = default)
    {
        return await apiTokenRepository.GetById(tokenId, cancellationToken);
    }

    public async Task<ApiToken> Create(ApiToken apiToken, CancellationToken cancellationToken = default)
    {
        apiToken =  await apiTokenRepository.Create(apiToken, cancellationToken) ??
               throw new AppException(ExceptionCodes.ApiTokenUnableToCreate);
        apiToken.Token = GenerateToken(apiToken);
        await apiTokenRepository.Update(apiToken);
        return apiToken;
    }

    private string GenerateToken(ApiToken apiToken)
    {
        var key = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(options.Value.Secret));
        var claims = GetApiKeyClaims(apiToken);

        var tokenDescriptor = new SecurityTokenDescriptor()
        {
            Issuer = options.Value.Issuer,
            Audience = "M2M",
            Subject = new ClaimsIdentity(claims),
            Expires = apiToken.ExpiredAt ?? DateTime.MaxValue,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature),
            NotBefore = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private IEnumerable<Claim>? GetApiKeyClaims(ApiToken apiToken)
    {
        yield return new Claim(ClaimTypes.NameIdentifier, apiToken.Id.ToString("D"));
    }

    public async Task<ApiToken> Update(Guid tokenId, string name, string? description, bool enabled, List<Policy> policies, CancellationToken cancellationToken = default)
    {
        var apiToken = await apiTokenRepository.GetById(tokenId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenNotFound);

        apiToken.Name = name;
        apiToken.Description = description;
        apiToken.Enabled = enabled;
        apiToken.Policies = policies;

        return await apiTokenRepository.Update(apiToken, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToUpdate);
    }

    public async Task<ApiToken> Delete(Guid tokenId, CancellationToken cancellationToken = default)
    {
        _ = await apiTokenRepository.GetById(tokenId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenNotFound);

        return await apiTokenRepository.Delete(tokenId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ApiTokenUnableToDelete);
    }
}
