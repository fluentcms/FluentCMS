namespace FluentCMS;

// TODO: move this to a project which include server side shared resources which will be accessible in any other server projects

/// <summary>
/// Defines the contract for accessing contextual information related to an API request.
/// Implementations of this interface provide details such as the user's authentication status,
/// request language, session ID, trace ID, and user identity information.
/// This interface is useful for consistent access to request-specific data throughout the application.
/// </summary>
public interface IApiExecutionContext
{
    /// <summary>
    /// Gets a value indicating whether the current user is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the preferred language of the current request, typically derived from the 
    /// "Accept-Language" header. Defaults to "en-US" if not provided.
    /// </summary>
    string Language { get; }

    /// <summary>
    /// Gets the unique session identifier associated with the current request,
    /// typically provided by the client in a custom header.
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// Gets the timestamp when the request was initiated, which can be used for logging 
    /// and calculating request duration.
    /// </summary>
    DateTime StartDate { get; }

    /// <summary>
    /// Gets the unique trace identifier for the current request, useful for tracing and 
    /// correlating logs across the system.
    /// </summary>
    string TraceId { get; }

    /// <summary>
    /// Gets the unique user identifier associated with the current request,
    /// typically provided by the client in a custom header.
    /// </summary>
    string UniqueId { get; }

    /// <summary>
    /// Gets the unique identifier of the authenticated user, extracted from the user's claims.
    /// Returns Guid.Empty if the user is not authenticated.
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// Gets the IP address of the user making the current request, which can be useful 
    /// for security purposes and logging.
    /// </summary>
    string UserIp { get; }

    /// <summary>
    /// Gets the username of the authenticated user, extracted from the user's claims.
    /// Returns an empty string if the user is not authenticated.
    /// </summary>
    string Username { get; }
}
