namespace IdentityExample.Services;

public interface ICurrentUser
{
    Guid? UserId { get; }
    User? User { get; }
    bool IsSuperAdmin { get; }
    bool IsAuthenticated { get; }
}
