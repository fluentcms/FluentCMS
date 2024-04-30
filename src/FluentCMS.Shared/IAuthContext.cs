namespace FluentCMS;

public interface IAuthContext
{
    string Username { get; }
    bool IsAuthenticated { get; }
    Guid UserId { get; }
}
