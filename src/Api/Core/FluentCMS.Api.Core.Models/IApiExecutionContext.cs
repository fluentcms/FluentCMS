namespace FluentCMS.Api.Core.Models;

/// <summary>
/// ApiExecutionContext encapsulates various contextual information 
/// about the current API request, such as trace ID, user identity, 
/// session ID, and more. This class is useful for logging, auditing, 
/// and controlling request-specific behavior.
/// </summary>
public interface IApiExecutionContext : IApplicationExecutionContext
{
    ApiToken ApiToken { get; }
    User? User { get; }
    List<Role> Roles { get; }
    Site? Site { get; }
    bool IsSuperAdmin { get; }
}
