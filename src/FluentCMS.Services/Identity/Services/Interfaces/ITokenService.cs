using FluentCMS.Entities.Identity;

namespace FluentCMS.Services.Identity;

public interface ITokenService
{
    Task<TokenResult> Generate(User user);
    Task<Guid> Validate(string accessToken);
    Task<Guid> ValidateExpiredToken(string accessToken);
}