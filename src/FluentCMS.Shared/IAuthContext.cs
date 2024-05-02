using FluentCMS.Entities;

namespace FluentCMS;

public interface IAuthContext
{
    Guid UserId { get; }
    string Username { get; }
    bool IsAuthenticated { get; }
    Task<User?> GetUser(CancellationToken cancellationToken = default);
    Task<List<Role>> GetRoles(CancellationToken cancellationToken = default);
}
