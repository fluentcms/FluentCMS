using FluentCMS.Entities;

namespace FluentCMS;

public interface IAuthContext
{
    Guid UserId { get; }
    string Username { get; }
    bool IsAuthenticated { get; }
    bool IsApi { get; }
    Task<User?> GetUser(CancellationToken cancellationToken = default);
    Task<ApiToken?> GetApiToken(CancellationToken cancellationToken = default);
    Task<List<Role>> GetRoles(CancellationToken cancellationToken = default);
}
