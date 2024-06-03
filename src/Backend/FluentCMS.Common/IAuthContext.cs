namespace FluentCMS;
public interface IAuthContext
{
    Guid UserId { get; }
    string Username { get; }
    bool IsAuthenticated { get; }
}

