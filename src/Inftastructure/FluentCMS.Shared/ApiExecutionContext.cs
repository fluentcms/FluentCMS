namespace FluentCMS;

public class ApiExecutionContext : IApplicationExecutionContext
{
    public string TraceId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UserIp { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; } = false;
}

public class SystemExecutionContext : IApplicationExecutionContext
{
    public string TraceId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UserIp { get; set; } = string.Empty;
    public string Language { get; set; } = "en-US";
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; } = false;
}