namespace FluentCMS.Infrastructure;

public interface IUserContext
{
    string Username { get; }       // Username extracted from the user's claims, default is empty string
}
