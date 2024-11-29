using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FluentCMS.Web.Api;

/// <summary>
/// ApiExecutionContext encapsulates various contextual information 
/// about the current API request, such as trace ID, user identity, 
/// session ID, and more. This class is useful for logging, auditing, 
/// and controlling request-specific behavior.
/// </summary>
public class ApiExecutionContext : IApiExecutionContext
{
    // Constants for HTTP header keys used to retrieve session and unique user identifiers
    public const string SESSION_ID_HEADER_KEY = "X_Session_Id";
    public const string UNIQUE_USER_ID_HEADER_KEY = "X-Unique-Id";
    public const string DEFAULT_LANGUAGE = "en-US";

    // Properties to store various contextual information
    public string TraceId { get; } = string.Empty;  // Unique identifier for the current request
    public string UniqueId { get; } = string.Empty; // Unique identifier for the user, often set by the client
    public string SessionId { get; } = string.Empty; // Unique session identifier
    public string UserIp { get; } = string.Empty;    // IP address of the user making the request
    public string Language { get; } = string.Empty;  // Preferred language of the user, defaults to 'en-US'
    public DateTime StartDate { get; } = DateTime.UtcNow; // Timestamp when the request was initiated
    public Guid UserId { get; } = Guid.Empty;       // User ID extracted from the user's claims, default is Guid.Empty
    public string Username { get; } = string.Empty; // Username extracted from the user's claims, default is empty string
    public bool IsAuthenticated { get; } = false;   // Indicates if the user is authenticated, default is false

    /// <summary>
    /// Constructor initializes the ApiExecutionContext with the current HTTP context information.
    /// </summary>
    /// <param name="accessor">IHttpContextAccessor to access the current HTTP context</param>
    public ApiExecutionContext(IHttpContextAccessor accessor)
    {
        // If the HttpContext is null, return early
        if (accessor?.HttpContext == null)
            return;

        var context = accessor.HttpContext;

        // Initialize properties based on the current HTTP context
        TraceId = context.TraceIdentifier;
        UniqueId = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(UNIQUE_USER_ID_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? string.Empty;
        SessionId = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(SESSION_ID_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? string.Empty;
        UserIp = context.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
        Language = context.Request?.GetTypedHeaders().AcceptLanguage.FirstOrDefault()?.Value.Value ?? DEFAULT_LANGUAGE;

        // Retrieve the user claims principal from the context
        var user = accessor.HttpContext?.User;

        if (user != null)
        {
            // Extract and parse the user ID from claims (ClaimTypes.Sid)
            var idClaimValue = user.FindFirstValue(ClaimTypes.Sid);
            UserId = idClaimValue == null ? Guid.Empty : Guid.Parse(idClaimValue);

            // Extract the username from claims (ClaimTypes.NameIdentifier)
            Username = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Determine if the user is authenticated
            IsAuthenticated = user.Identity?.IsAuthenticated ?? false;
        }
    }
}
