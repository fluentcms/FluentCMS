namespace FluentCMS.Infrastructure;

/// <summary>
/// IApplicationExecutionContext encapsulates various contextual information 
/// about the current API request, such as trace ID, user identity, 
/// session ID, and more. This class is useful for logging, auditing, 
/// and controlling request-specific behavior.
/// </summary>
public interface IApplicationExecutionContext
{
    bool IsAuthenticated { get; }  // Indicates if the user is authenticated, default is false
    string Language { get; }       // Preferred language of the user, defaults to 'en-US'
    string SessionId { get; }      // Unique session identifier
    DateTime StartDate { get; }    // Timestamp when the request was initiated
    string TraceId { get; }        // Unique identifier for the current request
    string UniqueId { get; }       // Unique identifier for the user, often set by the client
    Guid? UserId { get; }          // User ID extracted from the user's claims, default is Guid.Empty
    string UserIp { get; }         // IP address of the user making the request
    string Username { get; }       // Username extracted from the user's claims, default is empty string
}
