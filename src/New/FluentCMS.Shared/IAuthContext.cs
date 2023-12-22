namespace FluentCMS;

public interface IAuthContext
{
    IEnumerable<Guid> RoleIds { get; }
    string Username { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
}
