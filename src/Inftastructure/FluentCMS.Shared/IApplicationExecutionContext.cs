namespace FluentCMS;

/// <summary>
/// ApiExecutionContext encapsulates various contextual information 
/// about the current API request, such as trace ID, user identity, 
/// session ID, and more. This class is useful for logging, auditing, 
/// and controlling request-specific behavior.
/// </summary>
public interface IApplicationExecutionContext
{
    bool IsAuthenticated { get; set; }  // Indicates if the user is authenticated, default is false
    string Language { get; set; }       // Preferred language of the user, defaults to 'en-US'
    string SessionId { get; set; }      // Unique session identifier
    DateTime StartDate { get; set; }    // Timestamp when the request was initiated
    string TraceId { get; set; }        // Unique identifier for the current request
    string UniqueId { get; set; }       // Unique identifier for the user, often set by the client
    Guid? UserId { get; set; }           // User ID extracted from the user's claims, default is Guid.Empty
    string UserIp { get; set; }         // IP address of the user making the request
    string Username { get; set; }       // Username extracted from the user's claims, default is empty string
}
