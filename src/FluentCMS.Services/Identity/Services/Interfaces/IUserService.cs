using FluentCMS.Entities.Identity;
using System.Security.Claims;

namespace FluentCMS.Services.Identity;

public interface IUserService
{
    Task Create(User user, string password, CancellationToken cancellationToken = default);
    Task<User> Register(string username, string email, string password, CancellationToken cancellationToken = default);
    Task<SignInResult> Authenticate(string username, string password, CancellationToken cancellationToken = default);
    Task<Guid> GetCurrentUserId(CancellationToken cancellationToken = default);
    Task ChangePassword(User user, string newPassword, CancellationToken cancellationToken = default);
    Task ChangePassword(ChangePassword changePassword, CancellationToken cancellationToken = default);
    Task ForgotPassword(string username, CancellationToken cancellationToken = default);
    Task ResetPassword(string username, string token, string newPassword, CancellationToken cancellationToken = default);
    Task RevokeTokens(Guid id, CancellationToken cancellationToken = default);
    Task<SignInResult> RefreshToken(string refreshToken, string expiredToken, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAll(CancellationToken cancellationToken = default);
    Task<User> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Claim>> GetClaims(User user, CancellationToken cancellationToken = default);
    Task Update(User user, CancellationToken cancellationToken = default);
    Task<bool> ExistsUserName(string userName, CancellationToken cancellationToken = default);
}