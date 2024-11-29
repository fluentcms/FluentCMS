namespace FluentCMS.Entities.Logging;

public sealed class HttpLog : Entity
{
    public HttpRequestLog? Request { get; set; }
    public HttpResponseLog? Response { get; set; }
    public HttpException? Exception { get; set; }
    public int StatusCode { get; set; }
    public long Duration { get; set; }
    public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyVersion { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public int ThreadId { get; set; }
    public long MemoryUsage { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string EnvironmentUserName { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public string Language { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserIp { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ApiTokenKey { get; set; } = string.Empty;
}
