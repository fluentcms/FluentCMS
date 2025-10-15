namespace FluentCMS.Infrastructure;

public class ApiExecutionContext : IApplicationExecutionContext
{
    public string TraceId { get; init; } = default!;
    public string UniqueId { get; init; } = default!;
    public string SessionId { get; init; } = default!;
    public string UserIp { get; init; } = default!;
    public string Language { get; init; } = default!;
    public DateTime StartDate { get; } = DateTime.UtcNow;
    public Guid? UserId { get; init; }
    public string Username { get; init; } = default!;
    public bool IsAuthenticated { get; init; }
}

public class SystemExecutionContext : IApplicationExecutionContext
{
    public string TraceId { get; } = string.Empty;
    public string UniqueId { get; } = string.Empty;
    public string SessionId { get; } = string.Empty;
    public string UserIp { get; } = string.Empty;
    public string Language { get; } = "en-US";
    public DateTime StartDate { get; } = DateTime.UtcNow;
    public Guid? UserId { get; }
    public string Username { get; } = string.Empty;
    public bool IsAuthenticated { get; } = false;
}
