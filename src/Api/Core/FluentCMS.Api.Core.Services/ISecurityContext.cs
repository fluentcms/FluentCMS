namespace FluentCMS.Api.Core.Services;

/// <summary>
/// ISecurityContext encapsulates various contextual information 
/// about the current API request, such as trace ID, user identity, 
/// session ID, and more. This class is useful for logging, auditing, 
/// and controlling request-specific behavior.
/// </summary>
public interface ISecurityContext : IUserContext
{
    bool IsAuthenticated { get; }   // Indicates if the user is authenticated, default is false
    string Language { get; }        // Preferred language of the user, defaults to 'en-US'
    string SessionId { get; }       // Unique session identifier
    DateTime StartDate { get; }     // Timestamp when the request was initiated
    string TraceId { get; }         // Unique identifier for the current request
    string UniqueId { get; }        // Unique identifier for the user, often set by the client
    Guid? UserId { get; }           // User ID extracted from the user's claims
    string UserIp { get; }         // IP address of the user making the request
    string ClientIp { get; }       // IP address of the client making the request
    ApiToken? Token { get; }        // The API token associated with the request, if any
    User? User { get; }      // The user associated with the request, if authenticated
    List<Role> Roles { get; }   // List of roles associated with the user
    Site? Site { get; }      // The site associated with the request, if any
    List<Role> SiteRoles { get; }   // List of roles associated with the user for the specific site
}
