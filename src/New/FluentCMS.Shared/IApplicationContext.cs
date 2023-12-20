namespace FluentCMS;

public interface IApplicationContext
{
    IEnumerable<string> Roles { get; }
    string Username { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
}
